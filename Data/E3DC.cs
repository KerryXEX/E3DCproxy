using System.Net;
using AM.E3dc.Rscp;
using AM.E3dc.Rscp.Data;
using AM.E3dc.Rscp.Data.Values;

namespace E3dcproxy.Data;

public class E3DC
{
    //### Data objects   
    private E3dcConnection e3dcConnection = new();
    private SunServer mySrv = new();

    private ILogger<E3dcConnection> e3dcLogger { get; set; }
    public bool isConnected { get; set; }

    //### E3DC Constructor
    public E3DC(IConfiguration configuration)
    {
        mySrv.IP = configuration["E3DC-IP"] ?? "";
        mySrv.e3dcUserName = configuration["E3DC-User"] ?? "";
        mySrv.e3dcPassword = configuration["E3DC-Password"] ?? "";
        mySrv.rscpPassword = configuration["RSCP-Password"] ?? "";

        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Warning);
            builder.AddConsole();
        });
        e3dcLogger = loggerFactory.CreateLogger<E3dcConnection>();
    }

    public bool isAuthorized(HttpRequest context)
    {
        string apiKey = context?.Headers["X-API-Key"] ?? "";
        return apiKey == mySrv.rscpPassword;
    }

    //##############################
    //### Connect / Disconnect      
    //##############################
    //### Connect to E3DC and return connection object  
    public async Task<bool> Connect()
    {
        // Check if already connected  
        if (isConnected) return true;

        // Create the connector   
        e3dcConnection = new E3dcConnection(e3dcLogger);
        isConnected = false;

        // Connect to the E3/DC power station
        try
        {
            var endpoint = new IPEndPoint(IPAddress.Parse(mySrv.IP), 5033);
            await e3dcConnection.ConnectAsync(endpoint, mySrv.rscpPassword);
        }
        catch (Exception e)
        {
            Console.WriteLine("### E3DC Connect Error: " + e.Message);
            return false;
        }

        // Build the authorization frame
        var authFrame = new RscpFrame
        {
            new RscpContainer(RscpTag.RSCP_REQ_AUTHENTICATION)
            {
                new RscpString(RscpTag.RSCP_AUTHENTICATION_USER, mySrv.e3dcUserName),
                new RscpString(RscpTag.RSCP_AUTHENTICATION_PASSWORD, mySrv.e3dcPassword)
            }
        };

        // Send the frame to the power station and await the response     
        try
        {
            CancellationToken token = new CancellationTokenSource(5000).Token;
            var response = await e3dcConnection.SendAsync(authFrame, token);
            if (response.Values[0] is RscpUInt8 userLevelValue)
            {
                var userLevel = (RscpUserLevel)userLevelValue.Value;
                Console.WriteLine($"Authorization of '{mySrv.e3dcUserName}' successful (UserLevel: {userLevel}).");
                isConnected = true;
            }
            else
            {
                Console.WriteLine("Authorization of '{e3dcUserName}' failed or timed out");
                return false;
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            return false;
        }

        return isConnected;
    }

    public async Task Disconnect()
    {
        // Disconnect from the power station  
        isConnected = false;
        try
        {
            CancellationToken token = new CancellationTokenSource(5000).Token;
            await e3dcConnection.DisconnectAsync(token);
        }
        catch (Exception e)
        {
            Console.WriteLine("### E3DC Disconnect Error: " + e.Message);
        }
    }

    public async Task<RscpFrame?> SendAsync(RscpFrame requestFrame)
    {
        // Send the frame to the power station and return data array  
        RscpFrame myRes = new RscpFrame();
        CancellationToken token = new CancellationTokenSource(5000).Token;

        try
        {
            myRes = await e3dcConnection.SendAsync(requestFrame, token);
        }
        catch (Exception e)
        {
            //### Try again after reconnecting  
            if (await Connect())
            {
                try
                {
                    token = new CancellationTokenSource(3000).Token;
                    myRes = await e3dcConnection.SendAsync(requestFrame, token);
                }
                catch (Exception e2)
                {
                    Console.WriteLine("### E3DC Call Error in SendAsync: " + e2.Message);
                    myRes = null;
                }
            }
            else
            {
                Console.WriteLine("### E3DC Call Error in Reconnect after Exception: " + e.Message);
            }
        }

        return myRes;
    }

    //##############################
    //### Data retrieval functions  
    //##############################
    public async Task<SunServer> GetInfo()
    {
        // Build the response frame   
        var requestFrame = new RscpFrame
        {
            new RscpVoid(RscpTag.INFO_REQ_SERIAL_NUMBER),
            new RscpVoid(RscpTag.INFO_REQ_IP_ADDRESS),
            new RscpVoid(RscpTag.INFO_REQ_MAC_ADDRESS),
            new RscpVoid(RscpTag.INFO_REQ_SW_RELEASE),
            new RscpVoid(RscpTag.EMS_REQ_INSTALLED_PEAK_POWER),
            new RscpVoid(RscpTag.EP_REQ_IS_GRID_CONNECTED),
            new RscpContainer(RscpTag.PVI_REQ_DATA)
            {
                new RscpUInt16(RscpTag.PVI_INDEX, 0),
                new RscpVoid(RscpTag.PVI_REQ_DEVICE_STATE)
            },
            new RscpContainer(RscpTag.DCDC_REQ_DATA)
            {
                new RscpUInt16(RscpTag.DCDC_INDEX, 0),
                new RscpVoid(RscpTag.DCDC_REQ_DEVICE_STATE)
            }
        };

        // Send the frame to the power station and return data array  
        var myRes = await SendAsync(requestFrame) ?? new RscpFrame();
        var myBat = await GetBatteries();

        var srv = new SunServer
        {
            IP = myRes.Get<RscpString>(RscpTag.INFO_IP_ADDRESS).Value,
            NAME = myRes.Get<RscpString>(RscpTag.INFO_MAC_ADDRESS).Value,
            SERIAL = myRes.Get<RscpString>(RscpTag.INFO_SERIAL_NUMBER).Value,
            SUNPEAK = (int)myRes.Get<RscpUInt32>(RscpTag.EMS_INSTALLED_PEAK_POWER).Value,
            FWREL = myRes.Get<RscpString>(RscpTag.INFO_SW_RELEASE).Value,
            PVIstate = myRes.Get<RscpBool>(RscpTag.PVI_DEVICE_CONNECTED).Value,
            DCDCstate = myRes.Get<RscpBool>(RscpTag.DCDC_DEVICE_WORKING).Value,
            GRIDstate = myRes.Get<RscpBool>(RscpTag.EP_IS_GRID_CONNECTED).Value,
            BATCAP = myBat.Sum(b => b.DesignCapacity),
            BATUSABLE = myBat.Sum(b => b.UsableCapacity)
        };
        if (myBat.Count > 0)
        {
            srv.BATstate = myBat[0].isWorking;
        }

        return srv;
    }

    //### Get current States from E3DC   
    public async Task<SunState> GetStates()
    {
        // Build the response frame   
        var requestFrame = new RscpFrame
        {
            new RscpVoid(RscpTag.EMS_REQ_POWER_PV),
            new RscpVoid(RscpTag.EMS_REQ_POWER_BAT),
            new RscpVoid(RscpTag.EMS_REQ_POWER_HOME),
            new RscpVoid(RscpTag.EMS_REQ_POWER_GRID),
            new RscpVoid(RscpTag.EMS_REQ_POWER_ADD),
            new RscpVoid(RscpTag.EMS_REQ_POWER_WB_ALL),
            new RscpVoid(RscpTag.EMS_REQ_BAT_SOC),
            new RscpVoid(RscpTag.EMS_REQ_AUTARKY),
            new RscpVoid(RscpTag.EMS_REQ_SELF_CONSUMPTION)
        };

        // Send the frame to the power station and return data array  
        var myRes = await SendAsync(requestFrame) ?? new RscpFrame();

        var sunState = new SunState
        {
            sunPwr = myRes.Get<RscpInt32>(RscpTag.EMS_POWER_PV).Value,
            batPwr = myRes.Get<RscpInt32>(RscpTag.EMS_POWER_BAT).Value,
            homePwr = myRes.Get<RscpInt32>(RscpTag.EMS_POWER_HOME).Value,
            netPwr = myRes.Get<RscpInt32>(RscpTag.EMS_POWER_GRID).Value,
            autarky = myRes.Get<RscpFloat>(RscpTag.EMS_AUTARKY).Value,
            ownUse = myRes.Get<RscpFloat>(RscpTag.EMS_SELF_CONSUMPTION).Value,
            batLoad = myRes.Get<RscpUInt8>(RscpTag.EMS_BAT_SOC).Value,
            logDate = DateTime.Now
        };

        return sunState;
    }

    //### Get History Sum States from E3DC   
    public async Task<List<SunState>> GetHistSumStates()
    {
        // Build the response frame   
        var requestFrame = new RscpFrame();
        ulong today = ConvertToUnixTimestamp(DateTime.Today);
        ulong yesterday = ConvertToUnixTimestamp(DateTime.Today.AddDays(-1));
        ulong week = ConvertToUnixTimestamp(DateTime.Today.AddDays(-7));
        ulong month = ConvertToUnixTimestamp(DateTime.Today.AddDays(-30));
        ulong quarter = ConvertToUnixTimestamp(DateTime.Today.AddDays(-90));
        ulong year = ConvertToUnixTimestamp(DateTime.Today.AddDays(-365));
        ulong DaySpan = 24 * 60 * 60; // 1 day
        ulong WeekSpan = 7 * 24 * 60 * 60; // 1 week
        ulong MonthSpan = 30 * 24 * 60 * 60; // 1 month
        ulong QuarterSpan = 90 * 24 * 60 * 60; // 1 quarter
        ulong YearSpan = 365 * 24 * 60 * 60; // 1 year 

        requestFrame.Add(new RscpContainer(RscpTag.DB_REQ_HISTORY_DATA_DAY)
        {
            new RscpUInt64(RscpTag.DB_REQ_HISTORY_TIME_START, today),
            new RscpUInt64(RscpTag.DB_REQ_HISTORY_TIME_SPAN, DaySpan),
            new RscpUInt64(RscpTag.DB_REQ_HISTORY_TIME_INTERVAL, DaySpan)
        });
        requestFrame.Add(new RscpContainer(RscpTag.DB_REQ_HISTORY_DATA_DAY)
        {
            new RscpUInt64(RscpTag.DB_REQ_HISTORY_TIME_START, yesterday),
            new RscpUInt64(RscpTag.DB_REQ_HISTORY_TIME_SPAN, DaySpan),
            new RscpUInt64(RscpTag.DB_REQ_HISTORY_TIME_INTERVAL, DaySpan)
        });
        requestFrame.Add(new RscpContainer(RscpTag.DB_REQ_HISTORY_DATA_DAY)
        {
            new RscpUInt64(RscpTag.DB_REQ_HISTORY_TIME_START, week),
            new RscpUInt64(RscpTag.DB_REQ_HISTORY_TIME_SPAN, WeekSpan),
            new RscpUInt64(RscpTag.DB_REQ_HISTORY_TIME_INTERVAL, WeekSpan)
        });
        requestFrame.Add(new RscpContainer(RscpTag.DB_REQ_HISTORY_DATA_DAY)
        {
            new RscpUInt64(RscpTag.DB_REQ_HISTORY_TIME_START, month),
            new RscpUInt64(RscpTag.DB_REQ_HISTORY_TIME_SPAN, MonthSpan),
            new RscpUInt64(RscpTag.DB_REQ_HISTORY_TIME_INTERVAL, MonthSpan)
        });
        requestFrame.Add(new RscpContainer(RscpTag.DB_REQ_HISTORY_DATA_DAY)
        {
            new RscpUInt64(RscpTag.DB_REQ_HISTORY_TIME_START, quarter),
            new RscpUInt64(RscpTag.DB_REQ_HISTORY_TIME_SPAN, QuarterSpan),
            new RscpUInt64(RscpTag.DB_REQ_HISTORY_TIME_INTERVAL, QuarterSpan)
        });
        requestFrame.Add(new RscpContainer(RscpTag.DB_REQ_HISTORY_DATA_YEAR)
        {
            new RscpUInt64(RscpTag.DB_REQ_HISTORY_TIME_START, year),
            new RscpUInt64(RscpTag.DB_REQ_HISTORY_TIME_SPAN, YearSpan),
            new RscpUInt64(RscpTag.DB_REQ_HISTORY_TIME_INTERVAL, YearSpan)
        });


        // Send the frame to the power station and return data array  
        var myRes = await SendAsync(requestFrame) ?? new RscpFrame();

        //### Build data array from RSCP response    
        var myData = new List<SunState>();
        foreach (var resCnt in myRes)
        {
            var timeStamp = DateTime.Now;

            if (resCnt is RscpContainer myContainer)
            {
                if (myContainer.Children[0] is RscpContainer myContainer2)
                {
                    if (myContainer2.Tag == RscpTag.DB_SUM_CONTAINER)
                    {
                        var myDataItem = GetSunState(myContainer2, timeStamp, StatType.Sum);
                        myData.Add(myDataItem);
                    }
                }
            }
        }

        return myData;
    }

    //### Get History from E3DC   
    public async Task<List<SunState>> GetHistory(DateTime startDate, int days = 1,
        int interval = 900)
    {
        // Build the response frame   
        UInt64 DaySpan = 24 * 60 * 60; // 1 day 
        UInt64 myInt = (ulong)interval;
        var start = ConvertToUnixTimestamp(startDate);
        var reqStart = DateTime.Now;
        List<RscpFrame> res = new();
        List<LogDates> logDates = new();

        // Iterate through the requested number of days    
        for (int ix = 0; ix < days; ix++)
        {
            // Build request  
            var requestFrame = new RscpFrame();
            requestFrame.Add(new RscpContainer(RscpTag.DB_REQ_HISTORY_DATA_DAY)
            {
                new RscpUInt64(RscpTag.DB_REQ_HISTORY_TIME_START, start + (ulong)ix * DaySpan),
                new RscpUInt64(RscpTag.DB_REQ_HISTORY_TIME_SPAN, DaySpan),
                new RscpUInt64(RscpTag.DB_REQ_HISTORY_TIME_INTERVAL, myInt)
            });

            // Build log dates data   
            logDates.Add(new LogDates
            {
                Start = startDate.AddDays(ix),
                End = startDate.AddDays(ix + 1),
                ticks = (int)DaySpan / interval,
                success = false
            });

            // Send the frame to the power station and return data array
            var myRes = await SendAsync(requestFrame);
            if (myRes != null)
            {
                //### Check for valid response
                logDates[ix].success = true;
                if (days > 10)
                    Console.Write(".");
            }
            else
            {
                myRes = new RscpFrame();
            }

            res.Add(myRes);
        }

        Console.WriteLine("### res records = " + res.Count);

        var reqEnd = DateTime.Now;

        //### Build data array from RSCP response  
        var myData = new List<SunState>();
        var callType = days > 7 ? StatType.Day : StatType.Minute;
        int iy = 0;

        // Iterate through the response frames  
        foreach (var frame in res)
        {
            var timeStamp = logDates[iy].Start;
            if (frame is RscpFrame rCnt)
            {
                foreach (var resCnt in rCnt)
                {
                    if (resCnt is RscpContainer myContainer)
                    {
                        foreach (var res2Cnt in myContainer)
                        {
                            if (res2Cnt is RscpContainer myContainer2)
                            {
                                if (myContainer2.Tag == RscpTag.DB_VALUE_CONTAINER)
                                {
                                    if (myContainer2.Get<RscpFloat>(RscpTag.DB_GRAPH_INDEX).Value > 0)
                                    {
                                        var myDataItem = GetSunState(myContainer2, timeStamp, callType);
                                        myData.Add(myDataItem);
                                        timeStamp = timeStamp.AddSeconds(interval);
                                    }
                                }
                            }
                        }
                    }
                }
            }

            iy++;
        }

        Console.WriteLine($"Request duration: {reqEnd - reqStart} - {myData.Count} records");

        return myData;
    }

    //### Get Batteries from E3DC   
    public async Task<List<BatteryData>> GetBatteries()
    {
        // Build the response frame   
        var requestFrame = new RscpFrame();
        for (int ix = 0; ix < 8; ix++)
        {
            requestFrame.Add(new RscpContainer(RscpTag.BAT_REQ_DATA)
            {
                new RscpUInt16(RscpTag.BAT_INDEX, (byte)ix),
                new RscpVoid(RscpTag.BAT_REQ_DCB_COUNT),
            });
        }

        // Send the frame to the power station and return data array  
        var res = await SendAsync(requestFrame);
        List<BatteryData> myData = new();
        if (res == null) return myData;

        //### Build data array from RSCP response   
        foreach (var value in res)
        {
            if (value is RscpContainer myContainer)
            {
                var index = (int)myContainer.Get<RscpUInt16>(RscpTag.BAT_INDEX).Value;
                myData.Add(await GetBatteryData(index));
            }
        }

        return myData;
    }

    //### Get battery States from E3DC   
    private async Task<BatteryData> GetBatteryData(int index)
    {
        // Build the response frame   
        var requestFrame = new RscpFrame();
        requestFrame.Add(new RscpContainer(RscpTag.BAT_REQ_DATA)
        {
            new RscpUInt16(RscpTag.BAT_INDEX, (byte)index),
            new RscpVoid(RscpTag.BAT_REQ_DCB_INFO),
            new RscpVoid(RscpTag.BAT_REQ_DEVICE_STATE),
            new RscpVoid(RscpTag.BAT_REQ_DCB_COUNT),
            new RscpVoid(RscpTag.BAT_REQ_EOD_VOLTAGE),
            new RscpVoid(RscpTag.BAT_REQ_CHARGE_CYCLES),
            new RscpVoid(RscpTag.BAT_REQ_ERROR_CODE),
            new RscpVoid(RscpTag.BAT_REQ_DESIGN_CAPACITY),
            new RscpVoid(RscpTag.BAT_REQ_RSOC),
            new RscpVoid(RscpTag.BAT_REQ_RSOC_REAL),
            new RscpContainer(RscpTag.BAT_REQ_USABLE_CAPACITY)
            {
                new RscpVoid(RscpTag.BAT_REQ_USABLE_REMAINING_CAPACITY)
            }
        });

        // Send the frame to the power station and return data array  
        BatteryData myData = new();
        var ret = await SendAsync(requestFrame);
        if (ret == null) return myData;
        if (ret.Values[0] is RscpContainer res)
        {
            // Populate the battery data object   
            var voltage = res.Get<RscpFloat>(RscpTag.BAT_EOD_VOLTAGE).Value;
            myData.index = index;
            myData.isWorking = res.Get<RscpBool>(RscpTag.BAT_DEVICE_WORKING).Value;
            myData.Modules = res.Get<RscpUInt8>(RscpTag.BAT_DCB_COUNT).Value;
            myData.ChargeCycles = (int)res.Get<RscpUInt32>(RscpTag.BAT_CHARGE_CYCLES).Value;
            myData.ErrorCode = (int)res.Get<RscpUInt32>(RscpTag.BAT_ERROR_CODE).Value;
            myData.RSOC = res.Get<RscpFloat>(RscpTag.BAT_RSOC).Value;
            myData.RSOCREAL = res.Get<RscpFloat>(RscpTag.BAT_RSOC_REAL).Value;
            myData.DesignCapacity = res.Get<RscpFloat>(RscpTag.BAT_DESIGN_CAPACITY).Value * voltage;
            myData.UsableCapacity = res.Get<RscpFloat>(RscpTag.BAT_DCB_FULL_CHARGE_CAPACITY).Value * voltage;
        }

        Console.WriteLine(
            "### Battery: " + myData.index + " - " + myData.DesignCapacity + " - " + myData.UsableCapacity);
        return myData;
    }


    //##############################
    //### Data processing functions 
    //##############################


    //### Retrieve SunState from RSCP container   
    public SunState GetSunState(RscpContainer myContainer, DateTime timeStamp, StatType statType = StatType.Minute)
    {
        var myState = new SunState
        {
            homePwr = myContainer.Get<RscpFloat>(RscpTag.DB_CONSUMPTION).Value / 1000.0,
            batPwr = myContainer.Get<RscpFloat>(RscpTag.DB_BAT_POWER_IN).Value / 1000.0,
            batOut = myContainer.Get<RscpFloat>(RscpTag.DB_BAT_POWER_OUT).Value / 1000.0,
            batLoad = myContainer.Get<RscpFloat>(RscpTag.DB_BAT_CHARGE_LEVEL).Value,
            sunPwr = myContainer.Get<RscpFloat>(RscpTag.DB_DC_POWER).Value / 1000.0,
            netPwr = myContainer.Get<RscpFloat>(RscpTag.DB_GRID_POWER_OUT).Value / 1000.0,
            netOut = myContainer.Get<RscpFloat>(RscpTag.DB_GRID_POWER_IN).Value / 1000.0,
            autarky = myContainer.Get<RscpFloat>(RscpTag.DB_AUTARKY).Value,
            ownUse = myContainer.Get<RscpFloat>(RscpTag.DB_CONSUMED_PRODUCTION).Value,
            logDate = timeStamp,
            statType = statType
        };

        return myState;
    }

    //### Convert DateTime to Unix Timestamp   
    static UInt64 ConvertToUnixTimestamp(DateTime date) => (UInt64)date.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;
}

public class LogDates
{
    public DateTime Start { get; set; }
    public DateTime End { get; set; }
    public int ticks { get; set; }
    public bool success { get; set; }
}