﻿#region License
// Copyright (c) 2019 Jake Fowler
//
// Permission is hereby granted, free of charge, to any person 
// obtaining a copy of this software and associated documentation 
// files (the "Software"), to deal in the Software without 
// restriction, including without limitation the rights to use, 
// copy, modify, merge, publish, distribute, sublicense, and/or sell 
// copies of the Software, and to permit persons to whom the 
// Software is furnished to do so, subject to the following 
// conditions:
//
// The above copyright notice and this permission notice shall be 
// included in all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, 
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES 
// OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND 
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT 
// HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, 
// WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING 
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR 
// OTHER DEALINGS IN THE SOFTWARE.
#endregion

using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;
using MathNet.Numerics.Interpolation;

namespace Cmdty.Storage
{
    public sealed class PiecewiseLinearInjectWithdrawConstraint : IInjectWithdrawConstraint
    {
        private readonly InjectWithdrawRangeByInventory[] _injectWithdrawRanges;

        private readonly LinearSpline _maxInjectWithdrawLinear;
        private readonly LinearSpline _minInjectWithdrawLinear;

        public PiecewiseLinearInjectWithdrawConstraint([NotNull] IEnumerable<InjectWithdrawRangeByInventory> injectWithdrawRanges)
        {
            if (injectWithdrawRanges == null) throw new ArgumentNullException(nameof(injectWithdrawRanges));

            _injectWithdrawRanges = injectWithdrawRanges.OrderBy(injectWithdrawRange => injectWithdrawRange.Inventory)
                                                .ToArray();

            if (_injectWithdrawRanges.Length < 2)
                throw new ArgumentException("Inject/withdraw ranges collection must contain at least two elements.", nameof(injectWithdrawRanges));

            double[] inventories = _injectWithdrawRanges.Select(injectWithdrawRange => injectWithdrawRange.Inventory)
                                                        .ToArray();

            double[] maxInjectWithdrawRates = _injectWithdrawRanges
                                                    .Select(injectWithdrawRange => injectWithdrawRange.InjectWithdrawRange.MaxInjectWithdrawRate)
                                                    .ToArray();

            double[] minInjectWithdrawRates = _injectWithdrawRanges
                                                    .Select(injectWithdrawRange => injectWithdrawRange.InjectWithdrawRange.MinInjectWithdrawRate)
                                                    .ToArray();

            _maxInjectWithdrawLinear = LinearSpline.InterpolateSorted(inventories, maxInjectWithdrawRates);
            _minInjectWithdrawLinear = LinearSpline.InterpolateSorted(inventories, minInjectWithdrawRates);

        }

        public InjectWithdrawRange GetInjectWithdrawRange(double inventory)
        {
            double maxInjectWithdrawRate = _maxInjectWithdrawLinear.Interpolate(inventory);
            double minInjectWithdrawRate = _minInjectWithdrawLinear.Interpolate(inventory);
            return new InjectWithdrawRange(minInjectWithdrawRate, maxInjectWithdrawRate);
        }

        public double InventorySpaceUpperBound(double nextPeriodInventorySpaceLowerBound,
            double nextPeriodInventorySpaceUpperBound,
            double currentPeriodMinInventory, double currentPeriodMaxInventory, double inventoryPercentLoss)
        {
            var currentPeriodInjectWithdrawRangeAtMaxInventory = GetInjectWithdrawRange(currentPeriodMaxInventory);

            double nextPeriodMaxInventoryFromThisPeriodMaxInventory = currentPeriodMaxInventory * (1 - inventoryPercentLoss)
                                                                      + currentPeriodInjectWithdrawRangeAtMaxInventory.MaxInjectWithdrawRate;
            double nextPeriodMinInventoryFromThisPeriodMaxInventory = currentPeriodMaxInventory * (1 - inventoryPercentLoss)
                                                                      + currentPeriodInjectWithdrawRangeAtMaxInventory.MinInjectWithdrawRate;

            if (nextPeriodMinInventoryFromThisPeriodMaxInventory <= nextPeriodInventorySpaceUpperBound &&
                nextPeriodInventorySpaceLowerBound <= nextPeriodMaxInventoryFromThisPeriodMaxInventory)
            {
                // No need to solve root as next period inventory space can be reached from the current period max inventory
                return currentPeriodMaxInventory;
            }

            // Search for inventory bracket
            double bracketUpperInventory = _injectWithdrawRanges[_injectWithdrawRanges.Length - 1].Inventory;
            double bracketUpperInventoryAfterWithdraw = nextPeriodMinInventoryFromThisPeriodMaxInventory;
            for (int i = _injectWithdrawRanges.Length - 2; i >= 0; i--)
            {
                var bracketLowerDecisionRange = _injectWithdrawRanges[i];
                double bracketLowerInventory = bracketLowerDecisionRange.Inventory;
                double bracketLowerInventoryAfterWithdraw = bracketLowerInventory * (1 - inventoryPercentLoss) +
                                                bracketLowerDecisionRange.InjectWithdrawRange.MinInjectWithdrawRate;

                if (bracketLowerInventoryAfterWithdraw <= nextPeriodInventorySpaceUpperBound &&
                    nextPeriodInventorySpaceUpperBound <= bracketUpperInventoryAfterWithdraw)
                {
                    double inventorySpaceUpper = StorageHelper.InterpolateLinearAndSolve(bracketLowerInventory,
                                                bracketLowerInventoryAfterWithdraw, bracketUpperInventory,
                                                bracketUpperInventoryAfterWithdraw, nextPeriodInventorySpaceUpperBound);
                    return inventorySpaceUpper;
                }
                
                bracketUpperInventoryAfterWithdraw = bracketLowerInventoryAfterWithdraw;
                bracketUpperInventory = bracketLowerInventory;
            }
            
            throw new ApplicationException("Storage inventory constraints cannot be satisfied.");
        }

        public double InventorySpaceLowerBound(double nextPeriodInventorySpaceLowerBound, double nextPeriodInventorySpaceUpperBound,
                                                double currentPeriodMinInventory, double currentPeriodMaxInventory, double inventoryPercentLoss)
        {
            InjectWithdrawRange currentPeriodInjectWithdrawRangeAtMinInventory = GetInjectWithdrawRange(currentPeriodMinInventory);

            double nextPeriodMaxInventoryFromThisPeriodMinInventory = currentPeriodMinInventory * (1 - inventoryPercentLoss)
                                                                      + currentPeriodInjectWithdrawRangeAtMinInventory.MaxInjectWithdrawRate;
            double nextPeriodMinInventoryFromThisPeriodMinInventory = currentPeriodMinInventory * (1 - inventoryPercentLoss)
                                                                      + currentPeriodInjectWithdrawRangeAtMinInventory.MinInjectWithdrawRate;

            if (nextPeriodMinInventoryFromThisPeriodMinInventory <= nextPeriodInventorySpaceUpperBound &&
                nextPeriodInventorySpaceLowerBound <= nextPeriodMaxInventoryFromThisPeriodMinInventory)
            {
                // No need to solve root as next period inventory space can be reached from the current period min inventory
                return currentPeriodMinInventory;
            }

            // Search for inventory bracket
            double bracketLowerInventory = _injectWithdrawRanges[0].Inventory;
            double bracketLowerInventoryAfterInject= nextPeriodMaxInventoryFromThisPeriodMinInventory;

            for (int i = 1; i < _injectWithdrawRanges.Length; i++)
            {
                InjectWithdrawRangeByInventory bracketUpperDecisionRange = _injectWithdrawRanges[i];
                double bracketUpperInventory = bracketUpperDecisionRange.Inventory;
                double bracketUpperInventoryAfterInject = bracketUpperInventory * (1 - inventoryPercentLoss) +
                                            bracketUpperDecisionRange.InjectWithdrawRange.MaxInjectWithdrawRate;

                if (bracketLowerInventoryAfterInject <= nextPeriodInventorySpaceLowerBound &&
                    nextPeriodInventorySpaceLowerBound <= bracketUpperInventoryAfterInject)
                {
                    double inventorySpaceLower = StorageHelper.InterpolateLinearAndSolve(bracketLowerInventory,
                                                bracketLowerInventoryAfterInject, bracketUpperInventory, 
                                                bracketUpperInventoryAfterInject, nextPeriodInventorySpaceLowerBound);
                    return inventorySpaceLower;
                }

                bracketLowerInventoryAfterInject = bracketUpperInventoryAfterInject;
                bracketLowerInventory = bracketUpperInventory;
            }

            throw new ApplicationException("Storage inventory constraints cannot be satisfied.");
        }
    }
}
