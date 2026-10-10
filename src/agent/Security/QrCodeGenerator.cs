using System.Text;

namespace PulseCheck.Agent.Security;

public static class QrCodeGenerator
{
    private const int Version = 10;
    private const int Size = 57;
    private const int EccCodewordsPerBlock = 18;
    private static readonly int[] BlockDataLengths = [68, 68, 69, 69];
    private const int DataCodewords = 274;

    public static string GenerateSvg(string text)
    {
        var matrix = Build(text);
        var quiet = 4;
        var span = Size + quiet * 2;
        var sb = new StringBuilder();
        sb.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {span} {span}\" role=\"img\" aria-label=\"PulseCheck 配对二维码\" shape-rendering=\"crispEdges\">");
        sb.Append($"<rect width=\"100%\" height=\"100%\" fill=\"#fff\"/><path fill=\"#000\" d=\"");
        for (var y = 0; y < Size; y++)
            for (var x = 0; x < Size; x++)
                if (matrix[y, x]) sb.Append($"M{x + quiet} {y + quiet}h1v1h-1z");
        sb.Append("\"/></svg>");
        return sb.ToString();
    }

    private static bool[,] Build(string text)
    {
        var data = Encoding.UTF8.GetBytes(text);
        if (data.Length > 271) throw new ArgumentException("配对 URI 超过 QR 版本 10-L 的容量。", nameof(text));
        var dataCodewords = CreateDataCodewords(data);
        var allCodewords = AddErrorCorrection(dataCodewords);
        var bits = ToBits(allCodewords);
        var matrix = new bool[Size, Size];
        var functions = new bool[Size, Size];
        DrawFunctionPatterns(matrix, functions);
        DrawData(matrix, functions, bits);
        ApplyMask(matrix, functions);
        DrawFormatBits(matrix, functions);
        return matrix;
    }

    private static byte[] CreateDataCodewords(byte[] data)
    {
        var bits = new List<bool>(DataCodewords * 8);
        AppendBits(bits, 0b0100, 4);
        AppendBits(bits, data.Length, 16);
        foreach (var value in data) AppendBits(bits, value, 8);
        var capacityBits = DataCodewords * 8;
        AppendBits(bits, 0, Math.Min(4, capacityBits - bits.Count));
        while (bits.Count % 8 != 0) bits.Add(false);
        var result = new byte[DataCodewords];
        for (var i = 0; i < bits.Count; i++) if (bits[i]) result[i / 8] |= (byte)(1 << (7 - i % 8));
        var pad = new byte[] { 0xEC, 0x11 };
        for (var i = bits.Count / 8; i < result.Length; i++) result[i] = pad[(i - bits.Count / 8) % 2];
        return result;
    }

    private static byte[] AddErrorCorrection(byte[] data)
    {
        var blocks = new List<byte[]>();
        var ecc = new List<byte[]>();
        var offset = 0;
        var generator = ReedSolomonGenerator(EccCodewordsPerBlock);
        foreach (var length in BlockDataLengths)
        {
            var block = data.Skip(offset).Take(length).ToArray();
            offset += length;
            blocks.Add(block);
            ecc.Add(ReedSolomonRemainder(block, generator));
        }
        var result = new List<byte>(346);
        for (var i = 0; i < blocks.Max(x => x.Length); i++)
            foreach (var block in blocks)
                if (i < block.Length) result.Add(block[i]);
        for (var i = 0; i < EccCodewordsPerBlock; i++)
            foreach (var block in ecc) result.Add(block[i]);
        return [.. result];
    }

    private static byte[] ReedSolomonGenerator(int degree)
    {
        var result = new byte[degree + 1];
        result[0] = 1;
        for (var i = 0; i < degree; i++)
        {
            var next = new byte[degree + 1];
            for (var j = 0; j <= i; j++)
            {
                next[j] ^= result[j];
                next[j + 1] ^= GfMultiply(result[j], GfPow(2, i));
            }
            result = next;
        }
        return result;
    }

    private static byte[] ReedSolomonRemainder(byte[] data, byte[] generator)
    {
        var result = new byte[data.Length + generator.Length - 1];
        Array.Copy(data, result, data.Length);
        for (var i = 0; i < data.Length; i++)
        {
            var factor = result[i];
            if (factor == 0) continue;
            for (var j = 0; j < generator.Length; j++)
                result[i + j] ^= GfMultiply(generator[j], factor);
        }
        return result[^ (generator.Length - 1)..];
    }

    private static byte GfMultiply(byte x, byte y)
    {
        var value = (int)x;
        var result = 0;
        var multiplier = y;
        while (multiplier != 0)
        {
            if ((multiplier & 1) != 0) result ^= value;
            multiplier >>= 1;
            value <<= 1;
            if ((value & 0x100) != 0) value ^= 0x11D;
        }
        return (byte)result;
    }

    private static byte GfPow(byte value, int exponent)
    {
        var result = (byte)1;
        for (var i = 0; i < exponent; i++) result = GfMultiply(result, value);
        return result;
    }

    private static bool[] ToBits(byte[] bytes)
    {
        var bits = new bool[bytes.Length * 8];
        for (var i = 0; i < bytes.Length; i++)
            for (var j = 0; j < 8; j++)
                bits[i * 8 + j] = ((bytes[i] >> (7 - j)) & 1) != 0;
        return bits;
    }

    private static void DrawFunctionPatterns(bool[,] matrix, bool[,] functions)
    {
        DrawFinder(3, 3, matrix, functions);
        DrawFinder(Size - 4, 3, matrix, functions);
        DrawFinder(3, Size - 4, matrix, functions);
        for (var i = 8; i < Size - 8; i++)
        {
            SetFunction(matrix, functions, 6, i, i % 2 == 0);
            SetFunction(matrix, functions, i, 6, i % 2 == 0);
        }
        var align = new[] { 6, 28, 50 };
        foreach (var y in align)
            foreach (var x in align)
            {
                if ((x == 6 && y == 6) || (x == 6 && y == 50) || (x == 50 && y == 6)) continue;
                for (var dy = -2; dy <= 2; dy++)
                    for (var dx = -2; dx <= 2; dx++)
                        SetFunction(matrix, functions, x + dx, y + dy, Math.Max(Math.Abs(dx), Math.Abs(dy)) != 1);
            }
        for (var i = 0; i < 9; i++)
        {
            SetFunction(matrix, functions, 8, i, false);
            SetFunction(matrix, functions, i, 8, false);
        }
        for (var i = 0; i < 8; i++)
        {
            SetFunction(matrix, functions, Size - 1 - i, 8, false);
            SetFunction(matrix, functions, 8, Size - 1 - i, false);
        }
        SetFunction(matrix, functions, 8, 4 * Version + 9, true);
        DrawVersion(matrix, functions);
    }

    private static void DrawFinder(int centerX, int centerY, bool[,] matrix, bool[,] functions)
    {
        for (var dy = -4; dy <= 4; dy++)
            for (var dx = -4; dx <= 4; dx++)
            {
                var x = centerX + dx;
                var y = centerY + dy;
                if (x < 0 || y < 0 || x >= Size || y >= Size) continue;
                var dark = dx >= -3 && dx <= 3 && dy >= -3 && dy <= 3 &&
                           (Math.Abs(dx) == 3 || Math.Abs(dy) == 3 || (Math.Abs(dx) <= 1 && Math.Abs(dy) <= 1));
                SetFunction(matrix, functions, x, y, dark);
            }
    }

    private static void DrawVersion(bool[,] matrix, bool[,] functions)
    {
        var remainder = Version;
        for (var i = 0; i < 12; i++) remainder = (remainder << 1) ^ (((remainder >> 11) & 1) * 0x1F25);
        var bits = (Version << 12) | remainder;
        for (var i = 0; i < 18; i++)
        {
            var bit = ((bits >> i) & 1) != 0;
            var a = Size - 11 + i % 3;
            var b = i / 3;
            SetFunction(matrix, functions, a, b, bit);
            SetFunction(matrix, functions, b, a, bit);
        }
    }

    private static void DrawData(bool[,] matrix, bool[,] functions, bool[] bits)
    {
        var index = 0;
        for (var right = Size - 1; right >= 1; right -= 2)
        {
            if (right == 6) right = 5;
            for (var vertical = 0; vertical < Size; vertical++)
            {
                var upward = ((right + 1) & 2) == 0;
                var y = upward ? Size - 1 - vertical : vertical;
                for (var j = 0; j < 2; j++)
                {
                    var x = right - j;
                    if (functions[y, x]) continue;
                    matrix[y, x] = index < bits.Length && bits[index];
                    index++;
                }
            }
        }
    }

    private static void ApplyMask(bool[,] matrix, bool[,] functions)
    {
        for (var y = 0; y < Size; y++)
            for (var x = 0; x < Size; x++)
                if (!functions[y, x] && (x + y) % 2 == 0)
                    matrix[y, x] = !matrix[y, x];
    }

    private static void DrawFormatBits(bool[,] matrix, bool[,] functions)
    {
        const int mask = 0;
        var data = (0b01 << 3) | mask;
        var remainder = data;
        for (var i = 0; i < 10; i++) remainder = (remainder << 1) ^ (((remainder >> 9) & 1) * 0x537);
        var bits = ((data << 10) | remainder) ^ 0x5412;
        for (var i = 0; i <= 5; i++) SetFunction(matrix, functions, 8, i, GetBit(bits, i));
        SetFunction(matrix, functions, 8, 7, GetBit(bits, 6));
        SetFunction(matrix, functions, 8, 8, GetBit(bits, 7));
        SetFunction(matrix, functions, 7, 8, GetBit(bits, 8));
        for (var i = 9; i < 15; i++) SetFunction(matrix, functions, 14 - i, 8, GetBit(bits, i));
        for (var i = 0; i < 8; i++) SetFunction(matrix, functions, Size - 1 - i, 8, GetBit(bits, i));
        for (var i = 8; i < 15; i++) SetFunction(matrix, functions, 8, Size - 15 + i, GetBit(bits, i));
        SetFunction(matrix, functions, 8, Size - 8, true);
    }

    private static bool GetBit(int value, int index) => ((value >> index) & 1) != 0;

    private static void SetFunction(bool[,] matrix, bool[,] functions, int x, int y, bool dark)
    {
        if (x < 0 || y < 0 || x >= Size || y >= Size) return;
        matrix[y, x] = dark;
        functions[y, x] = true;
    }

    private static void AppendBits(List<bool> bits, int value, int count)
    {
        for (var i = count - 1; i >= 0; i--) bits.Add(((value >> i) & 1) != 0);
    }
}