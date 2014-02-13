using System;
using System.Collections.Generic;
using TradingMotion.SDK.Algorithms;
using TradingMotion.SDK.Algorithms.InputParameters;
using TradingMotion.SDK.Markets.Charts;
using TradingMotion.SDK.Markets.Orders;
using TradingMotion.SDK.Markets.Indicators.Momentum;
using TradingMotion.SDK.Markets.Indicators.OverlapStudies;

/// <summary>
/// Arya trading rules:
///     * Entry: Stochastic %D indicator breaks above an upper bound (buy signal) or below a lower bound (sell signal)
///     * Exit: Trailing stop based on the entry price and moving according to price raise, or price reaches Profit target
///     * Filters: Day-of-week trading enabled, trading timeframe, volatility filter, ADX minimum level and bullish/bearish trend
/// </summary>
namespace AryaStrategy
{
    public class AryaStrategy : Strategy
    {
        StochasticIndicator stochasticIndicator;
        ADXIndicator adxIndicator;
        SMAIndicator smaIndicator;

        Order trailingStopOrder;
        Order profitOrder;

        decimal acceleration;
        decimal furthestClose;

        public AryaStrategy(Chart mainChart, List<Chart> secondaryCharts)
            : base(mainChart, secondaryCharts)
        {

        }

        /// <summary>
        /// Strategy Name
        /// </summary>
        /// <returns>The complete name of the strategy</returns>
        public override string Name
        {
            get
            {
                return "Arya Strategy";
            }
        }

        /// <summary>
        /// Security filter that ensures the Position will be closed at the end of the trading session.
        /// </summary>
        public override bool ForceCloseIntradayPosition
        {
            get
            {
                return true;
            }
        }

        /// <summary>
        /// Security filter that sets a maximum open position size of 1 contract (either side)
        /// </summary>
        public override uint MaxOpenPosition
        {
            get
            {
                return 1;
            }
        }

        /// <summary>
        /// This strategy uses the Advanced Order Management mode
        /// </summary>
        public override bool UsesAdvancedOrderManagement
        {
            get
            {
                return true;
            }
        }

        /// <summary>
        /// Strategy Parameter definition
        /// </summary>
        public override InputParameterList SetInputParameters()
        {
            InputParameterList parameters = new InputParameterList();

            // Day of week trading enabled filters (0 = disabled)
            parameters.Add(new InputParameter("Monday Trading Enabled", 1));
            parameters.Add(new InputParameter("Tuesday Trading Enabled", 1));
            parameters.Add(new InputParameter("Wednesday Trading Enabled", 0));
            parameters.Add(new InputParameter("Thursday Trading Enabled", 0));
            parameters.Add(new InputParameter("Friday Trading Enabled", 1));

            // Session time filter (entries will be only placed during this time frame)
            parameters.Add(new InputParameter("Trading Time Start", new TimeSpan(18, 0, 0)));
            parameters.Add(new InputParameter("Trading Time End", new TimeSpan(6, 0, 0)));

            // The previous N bars period used for calculating Price Range
            parameters.Add(new InputParameter("Range Calculation Period", 10));

            // Minimum Volatility allowed for placing entries
            parameters.Add(new InputParameter("Minimum Range Filter", 0.002m));

            // The previous N bars period ADX indicator will use
            parameters.Add(new InputParameter("ADX Period", 14));
            // The previous N bars period SMA indicator will use
            parameters.Add(new InputParameter("SMA Period", 78));

            // Minimum ADX value for placing long entries
            parameters.Add(new InputParameter("Min ADX Long Entry", 12m));
            // Minimum ADX value for placing short entries
            parameters.Add(new InputParameter("Min ADX Short Entry", 12m));

            // The distance between the entry and the initial trailing stop price
            parameters.Add(new InputParameter("Trailing Stop Loss ticks distance", 24));
            // The initial acceleration of the trailing stop
            parameters.Add(new InputParameter("Trailing Stop acceleration", 0.2m));

            // The distance between the entry and the profit target level
            parameters.Add(new InputParameter("Profit Target ticks distance", 77));

            // The previous N bars period Stochastic indicator will use
            parameters.Add(new InputParameter("Stochastic Period", 68));
            // Break level of Stochastic %D we consider a buy signal
            parameters.Add(new InputParameter("Trend-following buy signal", 51m));
            // Break level of Stochastic %D we consider a sell signal
            parameters.Add(new InputParameter("Trend-following sell signal", 49m));

            return parameters;
        }

        /// <summary>
        /// Initialization method
        /// </summary>
        public override void OnInitialize()
        {
            log.Debug("AryaStrategy onInitialize()");

            // Adding a Stochastic indicator to strategy
            // (see http://stockcharts.com/help/doku.php?id=chart_school:technical_indicators:stochastic_oscillato)
            stochasticIndicator = new StochasticIndicator(Bars.Bars, (int)this.GetInputParameter("Stochastic Period"));
            this.AddIndicator("Stochastic Indicator", stochasticIndicator);

            // Adding an ADX indicator to strategy
            // (see http://www.investopedia.com/terms/a/adx.asp)
            adxIndicator = new ADXIndicator(Bars.Bars, (int)this.GetInputParameter("ADX Period"));
            this.AddIndicator("ADX Indicator", adxIndicator);

            // Adding a SMA indicator to strategy
            // (see http://www.investopedia.com/terms/s/sma.asp)
            smaIndicator = new SMAIndicator(Bars.Close, (int)this.GetInputParameter("SMA Period"));
            this.AddIndicator("SMA Indicator", smaIndicator);
        }

        /// <summary>
        /// Strategy enter/exit/filtering rules
        /// </summary>
        public override void OnNewBar()
        {
            decimal buySignal = (decimal)this.GetInputParameter("Trend-following buy signal");
            decimal sellSignal = (decimal)this.GetInputParameter("Trend-following sell signal");

            decimal stopMargin = (int)this.GetInputParameter("Trailing Stop Loss ticks distance") * this.GetMainChart().Symbol.TickSize;
            decimal profitMargin = (int)this.GetInputParameter("Profit Target ticks distance") * this.GetMainChart().Symbol.TickSize;

            bool longTradingEnabled = false;
            bool shortTradingEnabled = false;

            // Day-of-week filter
            if (IsDayEnabledForTrading(this.Bars.Time[0].DayOfWeek))
            {
                // Time-of-day filter
                if (IsTimeEnabledForTrading(this.Bars.Time[0]))
                {
                    // Volatility filter
                    if (CalculateVolatilityRange() > (decimal)this.GetInputParameter("Minimum Range Filter"))
                    {
                        // ADX minimum level and current trending filters
                        if (this.GetOpenPosition() == 0 && IsADXEnabledForLongEntry() && IsBullishUnderlyingTrend())
                        {
                            longTradingEnabled = true;
                        }
                        else if (this.GetOpenPosition() == 0 && IsADXEnabledForShortEntry() && IsBearishUnderlyingTrend())
                        {
                            shortTradingEnabled = true;
                        }
                    }
                }
            }

            if (longTradingEnabled && stochasticIndicator.GetD()[1] <= buySignal && stochasticIndicator.GetD()[0] > buySignal)
            {
                // BUY SIGNAL: Stochastic %D crosses above "buy signal" level
                MarketOrder buyOrder = new MarketOrder(OrderSide.Buy, 1, "Enter long position");
                this.InsertOrder(buyOrder);

                trailingStopOrder = new StopOrder(OrderSide.Sell, 1, this.Bars.Close[0] - stopMargin, "Catastrophic stop long exit");
                this.InsertOrder(trailingStopOrder);

                profitOrder = new LimitOrder(OrderSide.Sell, 1, this.Bars.Close[0] + profitMargin, "Profit stop long exit");
                this.InsertOrder(profitOrder);

                // Linking Stop and Limit orders: when one is executed, the other is cancelled
                trailingStopOrder.IsChildOf = profitOrder;
                profitOrder.IsChildOf = trailingStopOrder;

                // Setting the initial acceleration for the trailing stop and the furthest (the most extreme) close price
                acceleration = (decimal)this.GetInputParameter("Trailing Stop acceleration");
                furthestClose = this.Bars.Close[0];
            }
            else if (shortTradingEnabled && stochasticIndicator.GetD()[1] >= sellSignal && stochasticIndicator.GetD()[0] < sellSignal)
            {
                // SELL SIGNAL: Stochastic %D crosses below "sell signal" level
                MarketOrder sellOrder = new MarketOrder(OrderSide.Sell, 1, "Enter short position");
                this.InsertOrder(sellOrder);

                trailingStopOrder = new StopOrder(OrderSide.Buy, 1, this.Bars.Close[0] + stopMargin, "Catastrophic stop short exit");
                this.InsertOrder(trailingStopOrder);

                profitOrder = new LimitOrder(OrderSide.Buy, 1, this.Bars.Close[0] - profitMargin, "Profit stop short exit");
                this.InsertOrder(profitOrder);

                // Linking Stop and Limit orders: when one is executed, the other is cancelled
                trailingStopOrder.IsChildOf = profitOrder;
                profitOrder.IsChildOf = trailingStopOrder;

                // Setting the initial acceleration for the trailing stop and the furthest (the most extreme) close price
                acceleration = (decimal)this.GetInputParameter("Trailing Stop acceleration");
                furthestClose = this.Bars.Close[0];
            }
            else if (this.GetOpenPosition() == 1 && this.Bars.Close[0] > furthestClose)
            {
                // We're long and the price has moved in our favour

                furthestClose = this.Bars.Close[0];

                // Increasing acceleration
                acceleration = acceleration * (furthestClose - trailingStopOrder.Price);

                // Checking if trailing the stop order would exceed the current market price
                if (trailingStopOrder.Price + acceleration < this.Bars.Close[0])
                {
                    // Setting the new price for the trailing stop
                    trailingStopOrder.Price = trailingStopOrder.Price + acceleration;
                    trailingStopOrder.Label = "Trailing stop long exit";
                    this.ModifyOrder(trailingStopOrder);
                }
                else
                {
                    // Cancelling the order and closing the position
                    this.CancelOrder(trailingStopOrder);
                    this.CancelOrder(profitOrder);

                    MarketOrder exitLongOrder = new MarketOrder(OrderSide.Sell, 1, "Exit long position");
                    this.InsertOrder(exitLongOrder);
                }
            }
            else if (this.GetOpenPosition() == -1 && this.Bars.Close[0] < furthestClose)
            {
                // We're short and the price has moved in our favour

                furthestClose = this.Bars.Close[0];

                // Increasing acceleration
                acceleration = acceleration * Math.Abs(trailingStopOrder.Price - furthestClose);

                // Checking if trailing the stop order would exceed the current market price
                if (trailingStopOrder.Price - acceleration > this.Bars.Close[0])
                {
                    // Setting the new price for the trailing stop
                    trailingStopOrder.Price = trailingStopOrder.Price - acceleration;
                    trailingStopOrder.Label = "Trailing stop short exit";
                    this.ModifyOrder(trailingStopOrder);
                }
                else
                {
                    // Cancelling the order and closing the position
                    this.CancelOrder(trailingStopOrder);
                    this.CancelOrder(profitOrder);

                    MarketOrder exitShortOrder = new MarketOrder(OrderSide.Buy, 1, "Exit short position");
                    this.InsertOrder(exitShortOrder);
                }
            }
        }

        private bool IsDayEnabledForTrading(DayOfWeek currentDay)
        {
            // Check if current day is available to trade
            if (currentDay == DayOfWeek.Monday && (int)this.GetInputParameter("Monday Trading Enabled") == 0) return false;
            if (currentDay == DayOfWeek.Tuesday && (int)this.GetInputParameter("Tuesday Trading Enabled") == 0) return false;
            if (currentDay == DayOfWeek.Wednesday && (int)this.GetInputParameter("Wednesday Trading Enabled") == 0) return false;
            if (currentDay == DayOfWeek.Thursday && (int)this.GetInputParameter("Thursday Trading Enabled") == 0) return false;
            if (currentDay == DayOfWeek.Friday && (int)this.GetInputParameter("Friday Trading Enabled") == 0) return false;

            return true;
        }

        private bool IsTimeEnabledForTrading(DateTime currentBar)
        {
            // Check if the current bar's time of day is inside the enabled time range to trade
            TimeSpan tradingTimeStart = (TimeSpan)this.GetInputParameter("Trading Time Start");
            TimeSpan tradingTimeEnd = (TimeSpan)this.GetInputParameter("Trading Time End");

            if (tradingTimeStart <= tradingTimeEnd)
            {
                return currentBar.TimeOfDay >= tradingTimeStart &&
                    currentBar.TimeOfDay <= tradingTimeEnd;
            }
            else
            {
                return currentBar.TimeOfDay >= tradingTimeStart ||
                    currentBar.TimeOfDay <= tradingTimeEnd;
            }
        }

        private decimal CalculateVolatilityRange()
        {
            // Set current Range as Max(High) - Min(Low) of last "Range Calculation Period" bars
            int lookbackPeriod = (int)this.GetInputParameter("Range Calculation Period");
            return this.Bars.High.GetHighestValue(lookbackPeriod) - this.Bars.Low.GetLowestValue(lookbackPeriod);
        }

        private bool IsADXEnabledForLongEntry()
        {
            // Long entry enabled if ADX is above "Min ADX Long Entry" parameter
            return adxIndicator.GetADX()[0] >= (decimal)this.GetInputParameter("Min ADX Long Entry");
        }

        private bool IsBullishUnderlyingTrend()
        {
            // Consider bullish underlying trend if SMA has raised on the last bar
            return smaIndicator.GetAvSimple()[0] > smaIndicator.GetAvSimple()[1];
        }

        private bool IsADXEnabledForShortEntry()
        {
            // Short entry enabled if ADX is above "Min ADX Short Entry" parameter
            return adxIndicator.GetADX()[0] >= (decimal)this.GetInputParameter("Min ADX Short Entry");
        }

        private bool IsBearishUnderlyingTrend()
        {
            // Consider bearish underlying trend if SMA has fallen on the last bar
            return smaIndicator.GetAvSimple()[0] < smaIndicator.GetAvSimple()[1];
        }
    }
}
