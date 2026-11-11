using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;

namespace StreeckSuite.Core
{
    /// <summary>
    /// Generador de códigos QR propio (sin internet ni librerías): modo byte (UTF-8), corrección de errores M,
    /// versiones 1 a 20 (hasta ~660 bytes). Sigue el algoritmo del estándar ISO/IEC 18004 tal como lo implementa
    /// el generador de referencia de Project Nayuki (licencia MIT).
    /// </summary>
    internal sealed class QrCode
    {
        private const int MaxVersion = 20;
        // Nivel M: códigos de corrección por bloque y número de bloques, por versión (índice 0 sin uso)
        private static readonly int[] EccPerBlock = { -1, 10, 16, 26, 18, 24, 16, 18, 22, 22, 26, 30, 22, 22, 24, 24, 28, 28, 26, 26, 26 };
        private static readonly int[] NumBlocks = { -1, 1, 1, 1, 2, 2, 4, 4, 4, 5, 5, 5, 8, 9, 9, 10, 10, 11, 13, 14, 16 };

        public readonly int Version;
        public readonly int Size;
        private readonly bool[,] _modules;   // [y, x]
        private readonly bool[,] _function;

        public bool this[int x, int y] { get { return _modules[y, x]; } }

        /// <summary>
        /// null si el texto no cabe en la versión 20.
        /// </summary>
        public static QrCode Encode(string text)
        {
            byte[] data = Encoding.UTF8.GetBytes(text ?? "");
            for (int version = 1; version <= MaxVersion; version++)
            {
                int capacityBits = DataCodewords(version) * 8;
                int countBits = version <= 9 ? 8 : 16;
                if (4 + countBits + data.Length * 8 <= capacityBits) return new QrCode(version, data);
            }
            return null;
        }

        private QrCode(int version, byte[] data)
        {
            Version = version;
            Size = version * 4 + 17;
            _modules = new bool[Size, Size];
            _function = new bool[Size, Size];

            // Bits: modo byte, longitud, datos, terminador y relleno
            List<bool> bits = new List<bool>();
            Append(bits, 4, 4);
            Append(bits, data.Length, version <= 9 ? 8 : 16);
            foreach (byte b in data) Append(bits, b, 8);
            int capacity = DataCodewords(version) * 8;
            Append(bits, 0, Math.Min(4, capacity - bits.Count));
            Append(bits, 0, (8 - bits.Count % 8) % 8);
            for (int pad = 0xEC; bits.Count < capacity; pad ^= 0xEC ^ 0x11) Append(bits, pad, 8);
            byte[] codewords = new byte[bits.Count / 8];
            for (int i = 0; i < bits.Count; i++) if (bits[i]) codewords[i >> 3] |= (byte)(1 << (7 - (i & 7)));

            DrawFunctionPatterns();
            DrawCodewords(AddEccAndInterleave(codewords));

            // La máscara con menos penalización (más fácil de leer para la cámara)
            int best = 0, bestPenalty = int.MaxValue;
            for (int mask = 0; mask < 8; mask++)
            {
                ApplyMask(mask);
                DrawFormatBits(mask);
                int penalty = Penalty();
                if (penalty < bestPenalty) { best = mask; bestPenalty = penalty; }
                ApplyMask(mask); // Deshacer (XOR)
            }
            ApplyMask(best);
            DrawFormatBits(best);
        }

        /// <summary>
        /// Lo dibuja en negro sobre blanco con un margen de 4 módulos.
        /// </summary>
        public Bitmap ToBitmap(int pixelSize)
        {
            int quiet = 4, total = (Size + 2 * quiet) * pixelSize;
            Bitmap bitmap = new Bitmap(total, total);
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.Clear(Color.White);
                for (int y = 0; y < Size; y++)
                    for (int x = 0; x < Size; x++)
                        if (_modules[y, x]) g.FillRectangle(Brushes.Black, (x + quiet) * pixelSize, (y + quiet) * pixelSize, pixelSize, pixelSize);
            }
            return bitmap;
        }

        private static void Append(List<bool> bits, int value, int length)
        {
            for (int i = length - 1; i >= 0; i--) bits.Add(((value >> i) & 1) != 0);
        }

        private static int RawDataModules(int version)
        {
            int result = (16 * version + 128) * version + 64;
            if (version >= 2)
            {
                int numAlign = version / 7 + 2;
                result -= (25 * numAlign - 10) * numAlign - 55;
                if (version >= 7) result -= 36;
            }
            return result;
        }

        private static int DataCodewords(int version)
        {
            return RawDataModules(version) / 8 - EccPerBlock[version] * NumBlocks[version];
        }

        #region Patrones fijos

        private void SetFunction(int x, int y, bool dark)
        {
            _modules[y, x] = dark;
            _function[y, x] = true;
        }

        private void DrawFunctionPatterns()
        {
            for (int i = 0; i < Size; i++)
            {
                SetFunction(6, i, i % 2 == 0);
                SetFunction(i, 6, i % 2 == 0);
            }
            DrawFinder(3, 3);
            DrawFinder(Size - 4, 3);
            DrawFinder(3, Size - 4);

            int[] align = AlignmentPositions();
            int n = align.Length;
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++)
                    if (!(i == 0 && j == 0 || i == 0 && j == n - 1 || i == n - 1 && j == 0)) DrawAlignment(align[i], align[j]);

            DrawFormatBits(0); // Reserva el lugar; se reescribe con la máscara elegida
            DrawVersion();
        }

        private void DrawFinder(int x, int y)
        {
            for (int dy = -4; dy <= 4; dy++)
                for (int dx = -4; dx <= 4; dx++)
                {
                    int dist = Math.Max(Math.Abs(dx), Math.Abs(dy)), xx = x + dx, yy = y + dy;
                    if (xx >= 0 && xx < Size && yy >= 0 && yy < Size) SetFunction(xx, yy, dist != 2 && dist != 4);
                }
        }

        private void DrawAlignment(int x, int y)
        {
            for (int dy = -2; dy <= 2; dy++)
                for (int dx = -2; dx <= 2; dx++)
                    SetFunction(x + dx, y + dy, Math.Max(Math.Abs(dx), Math.Abs(dy)) != 1);
        }

        private int[] AlignmentPositions()
        {
            if (Version == 1) return new int[0];
            int numAlign = Version / 7 + 2;
            int step = (Version * 4 + numAlign * 2 + 1) / (numAlign * 2 - 2) * 2;
            int[] result = new int[numAlign];
            result[0] = 6;
            for (int i = numAlign - 1, pos = Size - 7; i >= 1; i--, pos -= step) result[i] = pos;
            return result;
        }

        private void DrawFormatBits(int mask)
        {
            int data = 0 << 3 | mask; // Nivel M = 00
            int rem = data;
            for (int i = 0; i < 10; i++) rem = (rem << 1) ^ ((rem >> 9) * 0x537);
            int bits = (data << 10 | rem) ^ 0x5412;

            for (int i = 0; i <= 5; i++) SetFunction(8, i, Bit(bits, i));
            SetFunction(8, 7, Bit(bits, 6));
            SetFunction(8, 8, Bit(bits, 7));
            SetFunction(7, 8, Bit(bits, 8));
            for (int i = 9; i < 15; i++) SetFunction(14 - i, 8, Bit(bits, i));

            for (int i = 0; i < 8; i++) SetFunction(Size - 1 - i, 8, Bit(bits, i));
            for (int i = 8; i < 15; i++) SetFunction(8, Size - 15 + i, Bit(bits, i));
            SetFunction(8, Size - 8, true); // Módulo oscuro fijo
        }

        private void DrawVersion()
        {
            if (Version < 7) return;
            int rem = Version;
            for (int i = 0; i < 12; i++) rem = (rem << 1) ^ ((rem >> 11) * 0x1F25);
            int bits = Version << 12 | rem;
            for (int i = 0; i < 18; i++)
            {
                bool bit = Bit(bits, i);
                int a = Size - 11 + i % 3, b = i / 3;
                SetFunction(a, b, bit);
                SetFunction(b, a, bit);
            }
        }

        private static bool Bit(int value, int i) { return ((value >> i) & 1) != 0; }

        #endregion

        #region Datos, corrección de errores y máscara

        private byte[] AddEccAndInterleave(byte[] data)
        {
            int numBlocks = NumBlocks[Version], eccLen = EccPerBlock[Version];
            int raw = RawDataModules(Version) / 8;
            int numShort = numBlocks - raw % numBlocks;
            int shortLen = raw / numBlocks;
            byte[] divisor = ReedSolomonDivisor(eccLen);

            byte[][] blocks = new byte[numBlocks][];
            for (int i = 0, k = 0; i < numBlocks; i++)
            {
                int len = shortLen - eccLen + (i < numShort ? 0 : 1);
                byte[] dat = new byte[len];
                Array.Copy(data, k, dat, 0, len);
                k += len;
                byte[] block = new byte[shortLen + 1];
                Array.Copy(dat, block, len);
                byte[] ecc = ReedSolomonRemainder(dat, divisor);
                Array.Copy(ecc, 0, block, block.Length - eccLen, eccLen);
                blocks[i] = block;
            }

            byte[] result = new byte[raw];
            for (int i = 0, k = 0; i < blocks[0].Length; i++)
                for (int j = 0; j < numBlocks; j++)
                    if (i != shortLen - eccLen || j >= numShort) result[k++] = blocks[j][i];
            return result;
        }

        private static byte[] ReedSolomonDivisor(int degree)
        {
            byte[] result = new byte[degree];
            result[degree - 1] = 1;
            int root = 1;
            for (int i = 0; i < degree; i++)
            {
                for (int j = 0; j < degree; j++)
                {
                    result[j] = (byte)Multiply(result[j], root);
                    if (j + 1 < degree) result[j] ^= result[j + 1];
                }
                root = Multiply(root, 0x02);
            }
            return result;
        }

        private static byte[] ReedSolomonRemainder(byte[] data, byte[] divisor)
        {
            byte[] result = new byte[divisor.Length];
            foreach (byte b in data)
            {
                int factor = b ^ result[0];
                Array.Copy(result, 1, result, 0, result.Length - 1);
                result[result.Length - 1] = 0;
                for (int i = 0; i < result.Length; i++) result[i] ^= (byte)Multiply(divisor[i], factor);
            }
            return result;
        }

        private static int Multiply(int x, int y)
        {
            int z = 0;
            for (int i = 7; i >= 0; i--)
            {
                z = (z << 1) ^ ((z >> 7) * 0x11D);
                z ^= ((y >> i) & 1) * x;
            }
            return z & 0xFF;
        }

        private void DrawCodewords(byte[] data)
        {
            int i = 0;
            for (int right = Size - 1; right >= 1; right -= 2)
            {
                if (right == 6) right = 5;
                for (int vert = 0; vert < Size; vert++)
                {
                    for (int j = 0; j < 2; j++)
                    {
                        int x = right - j;
                        bool upward = ((right + 1) & 2) == 0;
                        int y = upward ? Size - 1 - vert : vert;
                        if (!_function[y, x] && i < data.Length * 8)
                        {
                            _modules[y, x] = ((data[i >> 3] >> (7 - (i & 7))) & 1) != 0;
                            i++;
                        }
                    }
                }
            }
        }

        private void ApplyMask(int mask)
        {
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    if (_function[y, x]) continue;
                    bool invert;
                    switch (mask)
                    {
                        case 0: invert = (x + y) % 2 == 0; break;
                        case 1: invert = y % 2 == 0; break;
                        case 2: invert = x % 3 == 0; break;
                        case 3: invert = (x + y) % 3 == 0; break;
                        case 4: invert = (x / 3 + y / 2) % 2 == 0; break;
                        case 5: invert = x * y % 2 + x * y % 3 == 0; break;
                        case 6: invert = (x * y % 2 + x * y % 3) % 2 == 0; break;
                        default: invert = ((x + y) % 2 + x * y % 3) % 2 == 0; break;
                    }
                    if (invert) _modules[y, x] = !_modules[y, x];
                }
        }

        // Penalización simplificada (rachas, bloques 2x2 y equilibrio de oscuros)
        private int Penalty()
        {
            int result = 0, dark = 0;
            for (int a = 0; a < Size; a++)
            {
                int runRow = 0, runCol = 0;
                for (int b = 0; b < Size; b++)
                {
                    if (b > 0 && _modules[a, b] == _modules[a, b - 1]) runRow++; else { if (runRow >= 5) result += runRow - 2; runRow = 1; }
                    if (b > 0 && _modules[b, a] == _modules[b - 1, a]) runCol++; else { if (runCol >= 5) result += runCol - 2; runCol = 1; }
                    if (_modules[a, b]) dark++;
                }
                if (runRow >= 5) result += runRow - 2;
                if (runCol >= 5) result += runCol - 2;
            }
            for (int y = 0; y < Size - 1; y++)
                for (int x = 0; x < Size - 1; x++)
                {
                    bool c = _modules[y, x];
                    if (c == _modules[y, x + 1] && c == _modules[y + 1, x] && c == _modules[y + 1, x + 1]) result += 3;
                }
            int total = Size * Size;
            int k = (Math.Abs(dark * 20 - total * 10) + total - 1) / total - 1;
            return result + k * 10;
        }

        #endregion
    }
}
