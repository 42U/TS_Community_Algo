#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.SuperDom;
using NinjaTrader.Gui.Tools;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

//This namespace holds Strategies in this folder and is required. Do not change it. 
namespace NinjaTrader.NinjaScript.Strategies
{
    public class iFVG : Strategy
    {

        // variables for using ATM
        private string atmStrategyId;
        private string atmStrategyOrderId;
        private bool isAtmStrategyCreated = false;

        // RSI variables
        private RSI rsi;
        private double rsiValue;

        // variables for making sure only 1 order is submitted
        private bool inTrade = false;
        private int barNumber = -1;

        // List to store all detected Fair Value Gaps (FVGs)
        private List<FVG> fvgList = new List<FVG>();
        private double gapSize = 0;

        // variables for loss limits and profit limits
        private double UnrealProfit = 0;
        private int TradeNum = 0;
        private double DayPnl = 0;
        private double SessionPnl = 0;
        private double TradesAll = 0;
        private double ProfitReset = 0;
        private bool LimitHit = false;
        private bool LimitsAdjusted = false;

        // Power Switch for the Algo
        private bool PowerOn = false;

        // Session Times
        // Define trading session times in Eastern Time
        private TimeSpan asiaSessionStart = new TimeSpan(18, 02, 0); // 6:00 PM
        private TimeSpan asiaSessionEnd = new TimeSpan(1, 0, 0);    // 1:00 AM
        private TimeSpan londonSessionStart = new TimeSpan(3, 0, 0); // 3:00 AM
        private TimeSpan londonSessionEnd = new TimeSpan(11, 0, 0);  // 11:00 AM
        private TimeSpan newYorkAMSessionStart = new TimeSpan(9, 32, 0); // 9:30 AM
        private TimeSpan newYorkAMSessionEnd = new TimeSpan(12, 0, 0);   // 12:00 PM
        private TimeSpan newYorkPMSessionStart = new TimeSpan(13, 28, 0); // 1:30 PM
        private TimeSpan newYorkPMSessionEnd = new TimeSpan(16, 15, 0);   // 4:00 PM

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = @"TopStep Discord Community Strategy Build";
                Name = "TopStepCommunityAlgo";
                Calculate = Calculate.OnEachTick;
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds = 30;
                IsFillLimitOnTouch = false;
                MaximumBarsLookBack = MaximumBarsLookBack.TwoHundredFiftySix;
                OrderFillResolution = OrderFillResolution.Standard;
                Slippage = 0;
                StartBehavior = StartBehavior.WaitUntilFlat;
                TimeInForce = TimeInForce.Gtc;
                TraceOrders = false;
                RealtimeErrorHandling = RealtimeErrorHandling.StopCancelClose;
                StopTargetHandling = StopTargetHandling.PerEntryExecution;
                BarsRequiredToTrade = 20;

                // turn on off inverted fvgs / RSI
                UseiFVG = true;
                UseRSI = false;  // Add user input for RSI toggle
                RsiPeriod = 14;  // Default RSI period

                // variables for setting trade direction
                TradeDirection = @"long";       // short
                TradeType = @"market";  // limit
                TradeLimitPrice = 0;
                TradeStopPrice = 0;

                // Daily Limits
                UsingMicros = false;
                PreventOvertrade = true;   // if 50% of profit reached in a couple trades
                GetFlat = false;            // if LossLimit hit flatten and wait for next session
                DailyLossLimit = -1100.0;
                DailyProfitLimit = 5000.0;
                limitOffset = -50.0;

                // Decay Profit Target
                ProfitDecay = true;

                // Session Settings
                TradeAsia = false;
                TradeLondon = false;
                TradeNewYork = false; // this session treats AM and PM of NewYork as 1 session
                TradeNewYorkAM = true; // can select this to trade only AM
                TradeNewYorkPM = true; // can select this to trade only PM
                TradeNewYorkLunch = true; // can select this to trade only Lunch

                // Bars Since Last Trade
                barsSinceTrade = 0;
                maxTrades = 2;

                // iFVG variables
                lookBackCount = 3;
                bigTickEnergy = 0; // the lower the number the smaller the tick

                // your ATM template name needs to match this
                ATMname = @"ATMstrategy";
                // to use ATM strategies set to true
                ActivateATM = true;

            }
            else if (State == State.Configure)
            {
                Print($"Starting... {SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit}");
                if (UseRSI)
                {
                    rsi = RSI(RsiPeriod, 3);  // Set up the RSI with user-defined period
                    AddChartIndicator(rsi);    // Add the RSI indicator to the chart
                }
                // Use EMA as a condition to enter trade
            }
        }

        protected override void OnAccountItemUpdate(Cbi.Account account, Cbi.AccountItem accountItem, double value)
        {
            if (accountItem == Cbi.AccountItem.RealizedProfitLoss)
            {
                Print($" OnAcctItem = {account} = {value}");
                DayPnl = value;
            }
            else if (accountItem == Cbi.AccountItem.UnrealizedProfitLoss)
            {
                //Print($" UNREAL PROFIT ");
                UnrealProfit = value;
            }
        }

        protected override void OnConnectionStatusUpdate(ConnectionStatusEventArgs connectionStatusUpdate)
        {

        }

        protected override void OnExecutionUpdate(Cbi.Execution execution, string executionId, double price, int quantity,
            Cbi.MarketPosition marketPosition, string orderId, DateTime time)
        {

        }

        protected override void OnOrderUpdate(Cbi.Order order, double limitPrice, double stopPrice,
            int quantity, int filled, double averageFillPrice,
            Cbi.OrderState orderState, DateTime time, Cbi.ErrorCode error, string comment)
        {

        }

        protected override void OnPositionUpdate(Cbi.Position position, double averagePrice,
            int quantity, Cbi.MarketPosition marketPosition)
        {

        }

        protected override void OnBarUpdate()
        {
            // Live trading logic here
            // ---------------------------------

            // ATM strategy only works on realtime data
            if (State < State.Realtime)
                return;

            // 1. Identify important price levels (e.g., high and low of the day)
            // Example placeholder:
            //highOfDay = High[0]; // Replace with actual logic to calculate high of the day
            //lowOfDay = Low[0]; // Replace with actual logic to calculate low of the day

            // check for fvgs and add them to a list
            //if (UseiFVG)
            //{
            //    DetectFVG();
            //}

            // Ensure the RSI is enabled and initialized
            if (UseRSI && rsi != null)
            {
                rsiValue = rsi[0];  // Get the RSI value for the current bar
            }

            // Make sure we are in a trading session
            UpdateSessionFlags();

            // For risk management
            TrackPNL();

            // Make sure there is a few bars since last trade
            if (inTrade)
            {
                if (PositionAccount.MarketPosition == MarketPosition.Flat)
                {
                    inTrade = false;
                    barNumber = CurrentBar;

                    // Profit Target Decay with the number of trades taken
                    if (ProfitDecay)
                    {
                        if ((TradeNum <= maxTrades) && TradeNum > 0)
                        {
                            Print($"{TradeNum} less {maxTrades} {DailyProfitLimit} ");
                            DailyProfitLimit = (DailyProfitLimit + limitOffset);
                        }
                        else if (TradeNum >= maxTrades)
                        {
                            Print($"Over Trading");
                            if (HardLimit)
                            {
                                LimitHit = true;
                            }
                            else
                            {
                                DailyProfitLimit = DailyProfitLimit + (((TradeNum - maxTrades) + 1) * limitOffset);
                            }

                        }
                    }
                }
            }
            // 2. Check for conditions to trigger a trade
            if (TradeConditionMet() && !LimitHit && PowerOn && ((barNumber + barsSinceTrade) <= CurrentBar)) // Placeholder method for your trade condition
            {
                EnterTrade(); // Method to handle trade entry
            }

            // 3. Implement exit strategies (e.g., stop loss, take profit)
            if (!ActivateATM && (Position.MarketPosition == MarketPosition.Long))
            {
                //Placeholder for logic to handle long positions (e.g., exit conditions)
            }
            else if (!ActivateATM && (Position.MarketPosition == MarketPosition.Short))
            {
                //Placeholder for logic to handle short positions (e.g., exit conditions)
            }
        }


        #region Methods

        // Confirm when script is active that it is only trading the selected sessions
        private void UpdateSessionFlags()
        {
            DateTime currentTime = Times[0][0]; // Get the current bar time
            PowerOn = false;

            // Asia Session
            if ((currentTime.TimeOfDay >= asiaSessionStart || currentTime.TimeOfDay < asiaSessionEnd) && TradeAsia)
            {
                PowerOn = true;
            }

            // London Session
            if ((currentTime.TimeOfDay >= londonSessionStart && currentTime.TimeOfDay < londonSessionEnd) && TradeLondon)
            {
                PowerOn = true;
            }

            // New York AM Session
            if ((currentTime.TimeOfDay >= newYorkAMSessionStart && currentTime.TimeOfDay < newYorkAMSessionEnd) && TradeNewYorkAM)
            {
                PowerOn = true;
            }

            // New York PM Session
            if ((currentTime.TimeOfDay >= newYorkPMSessionStart && currentTime.TimeOfDay < newYorkPMSessionEnd) && TradeNewYorkPM)
            {
                PowerOn = true;
            }

            // New York Lunch Break
            if ((currentTime.TimeOfDay >= newYorkAMSessionEnd && currentTime.TimeOfDay < newYorkPMSessionStart) && TradeNewYorkLunch)
            {
                PowerOn = true;
            }
            // New York 1 Session
            if ((currentTime.TimeOfDay >= newYorkAMSessionStart && currentTime.TimeOfDay < newYorkPMSessionEnd) && TradeNewYork)
            {
                PowerOn = true;
            }
            if (PowerOn)
            {
                if (UseiFVG)
                {
                    DetectFVG();
                }
            }
        }

        // Track Day and Session PNL
        private void TrackPNL()
        {
            // Add combine settings. Separate Daily and Session PNL. 

            // Scale for micros
            if (UsingMicros && !LimitsAdjusted)
            {
                DailyLossLimit = (DailyLossLimit / 10);
                DailyProfitLimit = (DailyProfitLimit / 10);
                LimitsAdjusted = true;
            }

            // Set the profit target value for resetting after decay
            if ((ProfitDecay) && (ProfitReset == 0))
            {
                ProfitReset = DailyProfitLimit;
            }

            // A check to avoid overtrading and to see if you had a great run-up, to stop for the session and wait for the next
            if ((PreventOvertrade) && ((TradeNum <= 4) && (TradeNum > 0) && (SessionPnl > (DailyProfitLimit * .70)) || ((TradeNum == 1) && SessionPnl >= (DailyProfitLimit * .50))))
            {
                Print($"PNL Lockout - too much $$$$ after {TradeNum} trade(s)!");
                LimitHit = true;
            }

            // Should flatten all positions and orders and stop trading for the session
            if ((GetFlat) && (!LimitHit) && ((SessionPnl + UnrealProfit) <= DailyLossLimit))
            {
                AtmStrategyClose(atmStrategyId);
                Print($"Strategy closed {atmStrategyId}");
                LimitHit = true;
                Print($"Flattening ALL due to loss: {SessionPnl + UnrealProfit}");
            }

            // Check session PNL against the Day PNL
            if ((SessionPnl >= DailyProfitLimit) || (SessionPnl <= DailyLossLimit))
            {
                Print($"Limit Hit {LimitHit} pnl= {SessionPnl} trades={TradeNum}");
                LimitHit = true;
            }

            if (TradeAsia && Times[0][0].TimeOfDay == asiaSessionStart)
            {
                Print($"New Day & Asia Session");
                LimitHit = false;
                DayPnl = 0;
                SessionPnl = 0;
                TradeNum = 0;
                BackBrush = Brushes.Green;
                if (ProfitDecay)
                {
                    DailyProfitLimit = ProfitReset;
                }
            }
            else if (TradeLondon && Times[0][0].TimeOfDay == londonSessionStart)
            {

                Print($"London Session");
                LimitHit = false;
                TradesAll = DayPnl;
                SessionPnl = 0;
                TradeNum = 0;
                BackBrush = Brushes.Green;
                if (ProfitDecay)
                {
                    DailyProfitLimit = ProfitReset;
                }
            }
            else if (((TradeNewYorkAM || TradeNewYork) && Times[0][0].TimeOfDay == newYorkAMSessionStart))
            {
                Print($"New York AM Session");
                LimitHit = false;
                TradesAll = DayPnl;
                SessionPnl = 0;
                TradeNum = 0;
                BackBrush = Brushes.Green;
                //fvgList.Clear();
                if (ProfitDecay)
                {
                    DailyProfitLimit = ProfitReset;
                }
            }
            else if ((TradeNewYorkLunch && Times[0][0].TimeOfDay == newYorkAMSessionEnd) && (!TradeNewYorkAM && !TradeNewYork))
            {
                Print($"New York Lunch Break");
                LimitHit = false;
                TradesAll = DayPnl;
                SessionPnl = 0;
                TradeNum = 0;
                BackBrush = Brushes.Green;
                if (ProfitDecay)
                {
                    DailyProfitLimit = ProfitReset;
                }
            }
            else if ((TradeNewYorkPM && Times[0][0].TimeOfDay == newYorkPMSessionStart) && !TradeNewYork)
            {
                Print($"New York PM Session");
                LimitHit = false;
                TradesAll = DayPnl;
                SessionPnl = 0;
                TradeNum = 0;
                BackBrush = Brushes.Green;
                if (ProfitDecay)
                {
                    DailyProfitLimit = ProfitReset;
                }
            }
            else if (Times[0][0].TimeOfDay == new TimeSpan(15, 42, 0))
            {
                Print($"End Of Trading Day {DayPnl}");
                LimitHit = true;
                PowerOn = false;
                TradeNum = 0;
                TradesAll = DayPnl;
                BackBrush = Brushes.Orange;
                if (ProfitDecay)
                {
                    DailyProfitLimit = ProfitReset;
                }
            }
            // turn power off at end of each session
            else if ((TradeAsia && Times[0][0].TimeOfDay == asiaSessionEnd) || (TradeLondon && Times[0][0].TimeOfDay == londonSessionEnd) ||
                ((TradeNewYorkAM && (!TradeNewYorkLunch && !TradeNewYorkPM && !TradeNewYork)) && Times[0][0].TimeOfDay == newYorkAMSessionEnd) ||
                (TradeNewYorkPM && Times[0][0].TimeOfDay == newYorkPMSessionEnd) || (TradeNewYorkLunch && (Times[0][0].TimeOfDay == newYorkPMSessionStart) && !TradeNewYorkPM && !TradeNewYork))
            {
                Print($"End Of Session Or Day {DayPnl}");
                //LimitHit = true;
                PowerOn = false;
                TradeNum = 0;
                TradesAll = DayPnl;
                BackBrush = Brushes.Red;
                if (ProfitDecay)
                {
                    DailyProfitLimit = ProfitReset;
                }
            }
            SessionPnl = Math.Round(DayPnl - TradesAll, 2);
            Draw.TextFixed(this, @"pnl", Convert.ToString(SessionPnl), TextPosition.TopRight);
            Draw.TextFixed(this, @"numTrades", Convert.ToString(TradeNum), TextPosition.BottomLeft);
        }

        // Detect FVGs
        private void DetectFVG()
        {
            // Make sure there's enough historical data to compare
            if (CurrentBar < 3)
                return;

            gapSize = 0;
            // Check for Bullish Fair Value Gap
            if ((High[2] < Low[0]) && (Open[1] < Close[1]) && (Open[1] <= High[2]) && (Close[1] >= Low[0]))
            {
                // Check for gap size
                gapSize = (Low[0] - High[2]);
                if (gapSize >= bigTickEnergy)
                {
                    // Create and store a bullish FVG
                    FVG fvg = new FVG
                    {
                        Type = FVGType.Bullish,
                        StartBar = CurrentBar,
                        StartPrice = High[2],
                        EndPrice = Low[0]
                    };
                    fvgList.Add(fvg);

                    Print($"Bullish FVG detected from {High[2]} to {Low[0]} at bar {CurrentBar}");
                }
            }

            // Check for Bearish Fair Value Gap
            else if ((Low[2] > High[0]) && (Close[1] < Open[1]) && (Open[1] >= Low[2]) && (Close[1] <= High[0]))
            {
                // Check for gap size
                gapSize = (Low[2] - High[0]);
                if (gapSize >= bigTickEnergy)
                {
                    // Create and store a bearish FVG
                    FVG fvg = new FVG
                    {
                        Type = FVGType.Bearish,
                        StartBar = CurrentBar,
                        StartPrice = Low[2],
                        EndPrice = High[0]
                    };
                    fvgList.Add(fvg);

                    Print($"Bearish FVG detected from {Low[2]} to {High[0]} at bar {CurrentBar}");
                }
            }
        }
        // Example class to represent a Fair Value Gap (FVG)
        public class FVG
        {
            public FVGType Type { get; set; }
            public int StartBar { get; set; }
            public double StartPrice { get; set; }
            public double EndPrice { get; set; }
            public bool IsClosed { get; set; } = false;
        }

        // Enum to differentiate between bullish and bearish FVGs
        public enum FVGType
        {
            Bullish,
            Bearish
        }

        // Example placeholder method for trade condition logic
        private bool TradeConditionMet()
        {
            // Add logic to check for trade conditions (e.g., engulfing pattern, price breakout)
            if (!inTrade && PowerOn)
            {
                if (UseiFVG)
                {
                    if (CheckForFVGClosure())
                    {
                        if (UseRSI)
                        {
                            // Add RSI condition (e.g., RSI < 30 for long trades, RSI > 70 for short trades)
                            if (TradeDirection == "long" && rsiValue < 30)
                            {
                                return true;
                            }
                            if (TradeDirection == "short" && rsiValue > 70)
                            {
                                return true;
                            }
                        }
                        else
                        {
                            return true;
                        }
                    }
                }
            }
            return false;
        }

        // Function to check if the template name is created
        private bool DoesAtmStrategyTemplateExist(string templateName)
        {
            bool templateMatch = false;
            try
            {
                string template = ChartControl.OwnerChart.ChartTrader.AtmStrategy.Template;
                if (templateName == template)
                {
                    templateMatch = true;
                }
                else
                {
                    MessageBox.Show($"ATM Strategy {ATMname} is not the active template on Chart Trader!", "Alert", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception err)
            {
                Print(err.Message);
            }
            return templateMatch;
        }

        // ATM logic 
        private void useATM(bool isLong, bool isMarket, double limitPrice, double stopPrice)
        {

            // Check if the ATM strategy name is provided and matches the template name
            if (string.IsNullOrEmpty(ATMname))
            {
                Print("Error: ATM strategy name is empty.");
                return;
            }

            if (!DoesAtmStrategyTemplateExist(ATMname))
            {
                Print($"Error: ATM strategy template '{ATMname}' does not exist.");
                return;
            }

            // ATM variables
            OrderAction action = isLong ? OrderAction.Buy : OrderAction.Sell;
            OrderType orderType = isMarket ? OrderType.Market : OrderType.Limit;

            if (isMarket)
            {
                limitPrice = 0;
                stopPrice = 0;
            }

            atmStrategyId = GetAtmStrategyUniqueId();
            atmStrategyOrderId = GetAtmStrategyUniqueId();

            Print($"Using ATM Strategy Template: {ATMname}");

            AtmStrategyCreate(action, orderType, limitPrice, stopPrice, TimeInForce.Day,
                atmStrategyOrderId, ATMname, atmStrategyId, (atmCallbackErrorCode, atmCallbackId) => {

                    // checks that the call back is returned for the current atmStrategyId stored
                    if (atmCallbackId == atmStrategyId)
                    {
                        // check the atm call back for any error codes
                        if (atmCallbackErrorCode == Cbi.ErrorCode.NoError)
                        {
                            // if no error, set private bool to true to indicate the atm strategy is created
                            isAtmStrategyCreated = true;
                            Print("ATM strategy created successfully.");
                        }
                        else
                        {
                            Print($"Error creating ATM strategy: {atmCallbackErrorCode}");
                        }
                    }
                });

            if (isAtmStrategyCreated)
            {
                if (atmStrategyOrderId.Length > 0)
                {
                    string[] status = GetAtmStrategyEntryOrderStatus(atmStrategyOrderId);
                    // If the status call can't find the order specified, the return array length will be zero otherwise it will hold elements
                    if (status.GetLength(0) > 0)
                    {
                        // Print out some information about the order to the output window
                        Print("The entry order average fill price is: " + status[0]);
                        Print("The entry order filled amount is: " + status[1]);
                        Print("The entry order order state is: " + status[2]);

                        // If the order state is terminal, reset the order id value
                        if (status[2] == "Filled" || status[2] == "Cancelled" || status[2] == "Rejected")
                            atmStrategyOrderId = string.Empty;
                    }
                } // If the strategy has terminated reset the strategy id
                else if (atmStrategyId.Length > 0 && GetAtmStrategyMarketPosition(atmStrategyId) == Cbi.MarketPosition.Flat)
                    atmStrategyId = string.Empty;

                if (atmStrategyId.Length > 0)
                {
                    // You can change the stop price even when its being managed by the ATM strategy STOP19 is to prevent the STOP1 from being changed
                    // more logic needed here if you want to do fancy things with the stop loss while it is still being managed by ATM strategy

                    //if (GetAtmStrategyMarketPosition(atmStrategyId) != MarketPosition.Flat)
                    //	AtmStrategyChangeStopTarget(0, Low[0] - 3 * TickSize, "STOP19", atmStrategyId);

                    // Print some information about the strategy to the output window, please note you access the ATM strategy specific position object here
                    // the ATM would run self contained and would not have an impact on your NinjaScript strategy position and PnL
                    Print("The current ATM Strategy market position is: " + GetAtmStrategyMarketPosition(atmStrategyId));
                    Print("The current ATM Strategy position quantity is: " + GetAtmStrategyPositionQuantity(atmStrategyId));
                    Print("The current ATM Strategy average price is: " + GetAtmStrategyPositionAveragePrice(atmStrategyId));
                    Print("The current ATM Strategy Unrealized PnL is: " + GetAtmStrategyUnrealizedProfitLoss(atmStrategyId));
                }
            }
        }

        // CHECK FOR FVG CLOSE
        private bool CheckForFVGClosure()
        {
            // Make sure there are FVGs in the list before proceeding
            if (fvgList.Count == 0)
                return false;

            // Drop the oldest fair value gaps
            while (fvgList.Count > lookBackCount)
            {
                fvgList.RemoveAt(0);
            }

            // Loop through all FVGs in the list to check if they are closed
            for (int i = fvgList.Count - 1; i >= 0; i--)
            {
                FVG fvg = fvgList[i];

                // Only check FVGs that are still open (not closed)
                if (!fvg.IsClosed)
                {
                    // Check if the current bar's price closes the FVG
                    if (fvg.Type == FVGType.Bullish && Close[0] < fvg.StartPrice)
                    {
                        fvg.IsClosed = true; // Mark as closed
                        Print($"Bullish FVG closed at bar {CurrentBar}, StartPrice: {fvg.StartPrice}");
                        TradeDirection = "short";
                        TradeType = "market";
                        return true;
                    }
                    else if (fvg.Type == FVGType.Bearish && Close[0] > fvg.StartPrice)
                    {
                        fvg.IsClosed = true; // Mark as closed
                        Print($"Bearish FVG closed at bar {CurrentBar}, EndPrice: {fvg.EndPrice}");
                        TradeDirection = "long";
                        TradeType = "market";
                        return true;
                    }
                }
            }
            return false;
        }

        // Example method for entering a trade
        private void EnterTrade()
        {
            // Placeholder logic for entering a trade
            // Example:

            bool isLong = false;
            bool isMarket = false;

            if (TradeDirection == "long")
            {
                isLong = true;
            }
            if (TradeType == "market")
            {
                isMarket = true;
            }

            // use ATM for handling the target and stop
            if (ActivateATM)
            {
                useATM(isLong, isMarket, TradeLimitPrice, TradeStopPrice);
            }
            // flip the flag for in a trade
            inTrade = true;
            TradeNum += 1;
            //barNumber = CurrentBar;
            Print($"{Times[0][0].TimeOfDay} pnl= {DayPnl} stop={DailyLossLimit} profit={DailyProfitLimit}");

        }
        #endregion

        #region Properties
        // Each strategy might need some settings specific to that strat
        [NinjaScriptProperty]
        [Category("Strategy Settings")]
        [Display(Name = "UseiFVG", Order = 1, GroupName = "Strategy Settings")]
        public bool UseiFVG
        { get; set; }

        [NinjaScriptProperty]
        [Category("Strategy Settings")]
        [Display(Name = "Gap Size by Points", Order = 2, GroupName = "Strategy Settings")]
        public double bigTickEnergy
        { get; set; }

        [NinjaScriptProperty]
        [Category("Strategy Settings")]
        [Display(Name = "Use RSI in Strategy", Order = 3, GroupName = "Strategy Settings")]
        public bool UseRSI { get; set; }

        [NinjaScriptProperty]
        [Category("Strategy Settings")]
        [Range(1, int.MaxValue)]
        [Display(Name = "RSI Period", Order = 4, GroupName = "Strategy Settings")]
        public int RsiPeriod { get; set; }

        // Typical parameters that are needed but not often changed
        [NinjaScriptProperty]
        [Display(Name = "TradeDirection", Order = 1, GroupName = "Parameters")]
        public string TradeDirection
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "TradeType", Order = 2, GroupName = "Parameters")]
        public string TradeType
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "TradeLimitPrice", Order = 3, GroupName = "Parameters")]
        public double TradeLimitPrice
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "TradeStopPrice", Order = 4, GroupName = "Parameters")]
        public double TradeStopPrice
        { get; set; }


        [NinjaScriptProperty]
        [Display(Name = "lookBackCount", Order = 5, GroupName = "Parameters")]
        public int lookBackCount
        { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "barsSinceLastTrade", Order = 6, GroupName = "Parameters")]
        public int barsSinceTrade
        { get; set; }

        // Trade Settings related to modifying trade behavior
        [NinjaScriptProperty]
        [Category("Trade Settings")]
        [Display(Name = "ATMname", Order = 1, GroupName = "Trade Settings")]
        public string ATMname
        { get; set; }

        [NinjaScriptProperty]
        [Category("Trade Settings")]
        [Display(Name = "ActivateATM", Order = 2, GroupName = "Trade Settings")]
        public bool ActivateATM
        { get; set; }

        [NinjaScriptProperty]
        [Category("Trade Settings")]
        [Display(Name = "DailyLossLimit", Order = 3, GroupName = "Trade Settings")]
        public double DailyLossLimit
        { get; set; }

        [NinjaScriptProperty]
        [Category("Trade Settings")]
        [Display(Name = "DailyProfitLimit", Order = 4, GroupName = "Trade Settings")]
        public double DailyProfitLimit
        { get; set; }

        [NinjaScriptProperty]
        [Category("Trade Settings")]
        [Display(Name = "Flatten if LossLimit?", Order = 5, GroupName = "Trade Settings")]
        public bool GetFlat
        { get; set; }

        [NinjaScriptProperty]
        [Category("Trade Settings")]
        [Display(Name = "UsingMicros", Order = 6, GroupName = "Trade Settings")]
        public bool UsingMicros
        { get; set; }

        [NinjaScriptProperty]
        [Category("Trade Settings")]
        [Display(Name = "Profit Target Decay", Order = 7, GroupName = "Trade Settings")]
        public bool ProfitDecay
        { get; set; }

        [NinjaScriptProperty]
        [Category("Trade Settings")]
        [Display(Name = "Decay Amount", Order = 8, GroupName = "Trade Settings")]
        public double limitOffset
        { get; set; }

        [NinjaScriptProperty]
        [Category("Trade Settings")]
        [Display(Name = "maxTrades (increases profit decay)", Order = 9, GroupName = "Trade Settings")]
        public int maxTrades
        { get; set; }

        [NinjaScriptProperty]
        [Category("Trade Settings")]
        [Display(Name = "HardLimit (stop after maxTrades reached)", Order = 10, GroupName = "Trade Settings")]
        public bool HardLimit
        { get; set; }

        [NinjaScriptProperty]
        [Category("Trade Settings")]
        [Display(Name = "Prevent Overtrading if > 50% of target in < 4 trades", Order = 11, GroupName = "Trade Settings")]
        public bool PreventOvertrade
        { get; set; }


        // Parameters to select sessions
        [NinjaScriptProperty]
        [Display(Name = "Trade Asia Session", Order = 1, GroupName = "Session Settings")]
        public bool TradeAsia { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Trade London Session", Order = 2, GroupName = "Session Settings")]
        public bool TradeLondon { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Trade New York as 1 Session", Order = 3, GroupName = "Session Settings")]
        public bool TradeNewYork { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Trade New York AM Session", Order = 4, GroupName = "Session Settings")]
        public bool TradeNewYorkAM { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Trade New York PM Session", Order = 5, GroupName = "Session Settings")]
        public bool TradeNewYorkPM { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Trade New York Lunch Break", Order = 6, GroupName = "Session Settings")]
        public bool TradeNewYorkLunch { get; set; }

        #endregion

    }
}
