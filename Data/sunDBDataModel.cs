using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

// ReSharper disable UnusedAutoPropertyAccessor.Global
// ReSharper disable EntityFramework.ModelValidation.UnlimitedStringLength

namespace E3dcproxy.Data;

//###############################################
//### Datamodel Tasks, Processes                 
//###############################################

#region SunServer

//###############################################
//### Datamodel SunServer                        
//###############################################
public class SunServer
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public string IP { get; set; } = "";
    public string e3dcUserName { get; set; } = "";
    public string e3dcPassword { get; set; } = "";
    public string rscpPassword { get; set; } = "";
    public bool isValid { get; set; }

    public string NAME { get; set; } = "";
    public string PROD { get; set; } = "";
    public string TYPE { get; set; } = "";
    public string SERIAL { get; set; } = "";
    public string FWREL { get; set; } = "";
    public int SUNPEAK { get; set; }
    public double BATCAP { get; set; }
    public double BATUSABLE { get; set; }
    public bool PVIstate { get; set; }
    public bool BATstate { get; set; }
    public bool GRIDstate { get; set; }
    public bool DCDCstate { get; set; }
    public EmergencySwitch emgcyState { get; set; } = EmergencySwitch.EmgcyNotActive;
}

public class SunState
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int statId { get; set; }

    public StatType statType { get; set; } = StatType.Minute;

    public double sunPwr { get; set; }
    public double batPwr { get; set; }
    public double batOut { get; set; }
    public double homePwr { get; set; }
    public double netPwr { get; set; }
    public double netOut { get; set; }
    public double autarky { get; set; }
    public double ownUse { get; set; }
    public double batLoad { get; set; }
    public DateTime logDate { get; set; } = DateTime.Now;

    [NotMapped]
    public double batNet
    {
        get => batOut - batPwr;
    }

    [NotMapped]
    public double netNet
    {
        get => netPwr - netOut;
    }

    [NotMapped]
    public double stackPos
    {
        get
        {
            var sum = (batNet > 0) ? sunPwr + batNet : sunPwr;
            return (netNet > 0) ? sum + netNet : sum;
        }
    }

    [NotMapped]
    public double stackNeg
    {
        get
        {
            var sum = (batNet < 0) ? batNet : 0;
            return (netNet < 0) ? sum + netNet : sum;
        }
    }
}

public class DayPeak
{
    [Key]
    //[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    //public int Id { get; set; }
    public DateTime logDate { get; set; } = DateTime.Now;

    public double sunPwr { get; set; }
    public double batPwr { get; set; }
    public double homePwr { get; set; }
    public double netPwr { get; set; }
}

public class BatteryData
{
    public int index { get; set; }
    public bool isWorking { get; set; }
    public int Modules { get; set; }
    public int ChargeCycles { get; set; }
    public double DesignCapacity { get; set; }
    public double UsableCapacity { get; set; }
    public double RSOC { get; set; }
    public double RSOCREAL { get; set; }
    public int ErrorCode { get; set; }
};

public enum EmergencySwitch
{
    [Display(Name = "Nicht unterstützt")] NotSupported = 0,
    [Display(Name = "Notstrom Aktiv")] EmgcyActive = 1,

    [Display(Name = "Notstrom nicht aktiv")]
    EmgcyNotActive = 2,

    [Display(Name = "Notstrom nicht verfügbar")]
    EmgcyNotAvail = 3,
    [Display(Name = "Motorschalter aus")] MotorSwitch = 4
}

public enum StatType
{
    [Display(Name = "Minuten")] Minute = 0,
    [Display(Name = "Tage")] Day = 1,
    [Display(Name = "Summe")] Sum = 2
}

public class StateResult
{
    public SunState sunState { get; set; } = new();
    public DayPeak statPeak { get; set; } = new();
    public List<SunState> statSums { get; set; } = new();
}

#endregion

#region Tools

#endregion