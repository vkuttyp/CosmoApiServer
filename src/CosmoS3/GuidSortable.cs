namespace CosmoS3;

public class GuidSortable
{
    //UUID timestamps are 100ns ticks starting from October 15, 1582 (date we switched to Gregorian calendar)
    //Windows FILETIME is 100ns ticks starting from January 1, 1601  (the date of the start of the first 400-year cycle of the Gregorian calendar)
    private static readonly Int64 guidEpochOffset = -5748192000000000; //6653 days --> 159672 hours --> 9580320 minutes --> 574819200 seconds -> 574819200000 ms -> 574819200000000 us --> 5748192000000000 ticks

    private static Int64 _lastTick = DateTime.UtcNow.Ticks + guidEpochOffset;

    /// <summary>
    /// 
    /// </summary>
    /// <param name="location">The destination, whose value is compared with comparand and possibly replaced.</param>
    /// <param name="comparison">The value that is compared to the value at location1.</param>
    /// <param name="newValue">The value that replaces the destination value if the comparison results in equality.</param>
    /// <returns>true if comparand was greater than location, and location was updated to newValue. 
    /// Otherwise false.</returns>
    public static Boolean InterlockedExchangeIfGreaterThan(ref Int64 location, Int64 newValue, Int64 comparand)
    {
        //Thank you Raymond Chen: https://stackoverflow.com/a/13056904/12597
        Int64 currentValue;
        do
        {
            currentValue = Interlocked.Read(ref location); //a read of a 64-bit location is not atomic on x86. If location was 32-bit you could get away with just "currentValue = location;"
            if (currentValue >= comparand) return false;
        }
        while (System.Threading.Interlocked.CompareExchange(ref location, newValue, currentValue) != currentValue);
        return true;
    }

    /// <summary>
    /// Returns a new sortable guid. These guid's are suitable as clustering keys in SQL Server.
    /// </summary>
    /// <returns></returns>
    public static Guid NewGuid()
    {
        /*
            Blatently borrowed from Entity Framework.
                https://github.com/dotnet/efcore/blob/master/src/EFCore/ValueGeneration/SequentialGuidValueGenerator.cs


            Except with two differences:
                - they start with an initial timetime, generated statically once - and keep increasing that. 
                    That causes the "timestamp" to drift further and further from reality. 
                    We generate a timestamp each time, and only rely on incrementing if we're not greater than the last timestamp. 
                    A CPU is capable of generating ~200k GUIDs per 100ns tick - so it's not like we can ignore the duplciate ticks problem.
                - UUID timestamp ticks start at October 15, 1582 (date of gregorian calendar). 
                    Windows, and DateTime.Ticks returns number of ticks since January 1, 1601 (the date of the first 400 year Gregorian cycle). 
                    We'll offset the timestamp to match the UUID spec.
                - We place the version number type-7: Sortable by SQL Server with a timestamp.

            SQL Server sorts first by the NodeID (i.e. the final 6-bytes):

            16-byte guid                                            Microsoft clsid string representation
            ===========--=====--=====--=====--=================     ======================================
            33 22 11 00  55 44  77 66  99 88  ff ee dd cc bb aa ==> {00112233-4455-6677-9988-ffeeddccbbaa}
            \_______________________/  \___/  \_______________/
            Timestamp and Version  ^    Clk        Node ID

            The goal is to create a sequential uuid (e.g. UuidCreateSequential), but without having to rely on a MAC address. 
            (i.e. Does an Azure web-site even **have** a MAC address? 
            We certainly can't P/Invoke out to UuidCreateSequental when we're not running on Windows)

            So we conceptually move the 8-byte timestamp to it's new location in NodeID+ClockSequence

            And what used to be Timestamp+Version becomes random.

            And, like type-4 Uuids being called Type-4 to help reduce the chances of collisions between types,
            we call this new version type-7 (sortable by SQL Server with a timestamp).
        */

        //Start from a new random (type 4) uuid. 
        Byte[] guidBytes = Guid.NewGuid().ToByteArray();

        //Generate 8-byte timestamp
        Int64 currentTicks = DateTime.UtcNow.Ticks + guidEpochOffset;
        //Make sure that currentTicks is greater than the last tick count (in case they're generating guids faster than 100ns apart)
        Int64 last;
        do
        {
            last = Interlocked.Read(ref _lastTick); //a read of a 64-bit value isn't atomic; so it has to be interlocked.
            if (currentTicks <= last)
                currentTicks = last + 1;
        } while (Interlocked.CompareExchange(ref _lastTick, currentTicks, last) != last);


        Byte[] counterBytes = BitConverter.GetBytes(currentTicks);

        if (!BitConverter.IsLittleEndian)
        {
            Array.Reverse(counterBytes);
        }

        //This Guid started it's life as a Type 4 (Random) guid, but the final 8 bytes were changed to contain a sequential counter.
        //I want to tag this as a different kind of Uuid algorithm. In Delphi we already called this a Type 7 uuid.
        guidBytes[07] = (Byte)(0x70 | (guidBytes[07] & 0x0f));

        //Put the timestamp in place - in the proper SQL Server sorting order.
        guidBytes[08] = counterBytes[1];
        guidBytes[09] = counterBytes[0];
        guidBytes[10] = counterBytes[7];
        guidBytes[11] = counterBytes[6];
        guidBytes[12] = counterBytes[5];
        guidBytes[13] = counterBytes[4];
        guidBytes[14] = counterBytes[3];
        guidBytes[15] = counterBytes[2];

        Guid result = new Guid(guidBytes);
        return result;
    }
}