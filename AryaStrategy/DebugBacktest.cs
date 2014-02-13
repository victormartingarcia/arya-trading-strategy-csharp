﻿using System;
using System.Configuration;
using TradingMotion.SDK.Algorithms.Debug;
using TradingMotion.SDK.Markets;
using TradingMotion.SDK.Markets.Charts;
using TradingMotion.SDK.Markets.Symbols;
using TradingMotion.SDK.WebServices;

namespace AryaStrategy
{
    class DebugBacktest
    {
        static void Main(string[] args)
        {

            /* IMPORTANT INFORMATION
             * =====================
             * 
             * The purpose of this console application is to allow you to test/debug the Strategy
             * while you are developing it. 
             * 
             * Running this project will perform an EuroFX 6 month backtest on the Strategy, using 30 min bars.
             * 
             * Once the backtest is finished, you will be able to launch the TradingMotionSDKToolkit 
             * application to see the graphical result.
             * 
             * If you want to debug your code you can place breakpoints on the Strategy subclass
             * and Debug the project.
             * 
             * 
             * REQUIRED CREDENTIALS: Edit your app.config and enter your login/password for accessing the TradingMotion API
            */

            DateTime startBacktestDate = DateTime.Parse(DateTime.Now.AddMonths(-6).AddDays(-1).ToShortDateString() + " 00:00:00");
            DateTime endBacktestDate = DateTime.Parse(DateTime.Now.AddDays(-1).ToShortDateString() + " 23:59:59");

            TradingMotionAPIClient.Instance.SetUp("https://www.tradingmotion.com/api/webservice.asmx", ConfigurationManager.AppSettings["TradingMotionAPILogin"], ConfigurationManager.AppSettings["TradingMotionAPIPassword"]); //Enter your TradingMotion credentials on the app.config file
            HistoricalDataAPIClient.Instance.SetUp("http://barserver.tradingmotion.com/WSHistoricalData/webservice.asmx");

            AryaStrategy s = new AryaStrategy(new Chart(SymbolFactory.GetSymbol("URO"), BarPeriodType.Minute, 30), null);

            DebugStrategy.RunBacktest(s, startBacktestDate, endBacktestDate);

        }
    }
}
