namespace JanD.Lib.Objects;

[Flags]
public enum DaemonEvents
{
    // outlog
    OutLog = 0b0000_0001,

    // errlog
    ErrLog = 0b0000_0010,

    // procstop
    ProcessStopped = 0b0000_0100,

    // procstart
    ProcessStarted = 0b0000_1000,

    // procadd
    ProcessAdded = 0b0001_0000,

    // procdel
    ProcessDeleted = 0b0010_0000,

    // procren
    ProcessRenamed = 0b0100_0000,

    // procprop
    ProcessPropertyUpdated = 0b1000_0000,
}