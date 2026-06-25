namespace NetSearch.Core.Native;

public static class DataRunParser
{
    public static IReadOnlyList<DataRun> Parse(ReadOnlySpan<byte> runList)
    {
        var runs = new List<DataRun>();
        var pos = 0;
        long lcn = 0;
        while (pos < runList.Length)
        {
            var header = runList[pos++];
            if (header == 0) break;
            int lenSize = header & 0x0F;
            int offSize = (header >> 4) & 0x0F;
            if (lenSize == 0 || pos + lenSize + offSize > runList.Length) break;

            long length = 0;
            for (var i = 0; i < lenSize; i++) length |= (long)runList[pos++] << (8 * i);

            if (offSize == 0)
            {
                runs.Add(new DataRun(-1, length)); // sparse: LCN base unchanged
                continue;
            }

            long delta = 0;
            for (var i = 0; i < offSize; i++) delta |= (long)runList[pos++] << (8 * i);
            // sign-extend the offset field
            var signBit = 1L << (offSize * 8 - 1);
            if ((delta & signBit) != 0) delta -= signBit << 1;

            lcn += delta;
            runs.Add(new DataRun(lcn, length));
        }
        return runs;
    }
}
