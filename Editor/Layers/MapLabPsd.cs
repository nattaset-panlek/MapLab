using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace MapLab.Editor
{
    public static class MapLabPsd
    {
        #region [Variables/Fields]
        public const int MaxSide = 30000;
        public const string NormalBlend = "norm";
        public const string PassBlend = "pass";

        private const string GroupEndName = "</Layer group>";
        private const byte HiddenFlag = 0x02;
        private const byte GroupFlags = 0x18;
        private const uint LockedFlags = 0x07;
        private static readonly int[] ChannelIds = { -1, 0, 1, 2 };

        public sealed class Layer
        {
            public string Name = "";
            public bool Visible = true;
            public bool Locked;
            public byte Opacity = 255;
            public string BlendMode = NormalBlend;
            public bool Clipped;
            public bool HasMask;
            public RectInt Rect;
            public byte[] Pixels;
            public List<Layer> Children;

            public bool IsGroup => Children != null;
        }

        public sealed class Document
        {
            public int Width;
            public int Height;
            public List<Layer> Layers = new List<Layer>();
        }

        private sealed class Record
        {
            public Layer Source;
            public int Section;
            public string Name;
            public byte[][] Channels;
        }
        #endregion

        #region [Public Methods]
        public static void Write(string path, Document document, byte[] composite)
        {
            if (document.Width <= 0 || document.Height <= 0 || document.Width > MaxSide || document.Height > MaxSide)
            {
                throw new InvalidOperationException($"A PSD can be at most {MaxSide} px per side; this canvas is {document.Width} x {document.Height}.");
            }
            var records = new List<Record>();
            Flatten(document.Layers, records);

            using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write))
            {
                var writer = new BinaryWriter(stream);
                writer.Write(Encoding.ASCII.GetBytes("8BPS"));
                WriteU16(writer, 1);
                writer.Write(new byte[6]);
                WriteU16(writer, 4);
                WriteU32(writer, (uint)document.Height);
                WriteU32(writer, (uint)document.Width);
                WriteU16(writer, 8);
                WriteU16(writer, 3);
                WriteU32(writer, 0);
                WriteU32(writer, 0);

                byte[] layerInfo = LayerInfo(records);
                WriteU32(writer, (uint)(4 + layerInfo.Length + 4));
                WriteU32(writer, (uint)layerInfo.Length);
                writer.Write(layerInfo);
                WriteU32(writer, 0);

                WriteComposite(writer, composite, document.Width, document.Height);
            }
        }

        public static Document Read(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read))
            {
                var reader = new BinaryReader(stream);
                if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != "8BPS") throw new InvalidDataException("This is not a Photoshop file.");
                int version = ReadU16(reader);
                if (version == 2) throw new InvalidDataException("This is a PSB (large document). Save it as a PSD to import it.");
                if (version != 1) throw new InvalidDataException($"Unknown PSD version {version}.");
                reader.ReadBytes(6);
                ReadU16(reader);
                var document = new Document { Height = (int)ReadU32(reader), Width = (int)ReadU32(reader) };
                int depth = ReadU16(reader);
                int mode = ReadU16(reader);
                if (depth != 8) throw new InvalidDataException($"The file is {depth} bits per channel. Set Image > Mode > 8 Bits/Channel and save again.");
                if (mode != 3) throw new InvalidDataException("The file is not RGB. Set Image > Mode > RGB Color and save again.");
                Skip(reader, ReadU32(reader));
                Skip(reader, ReadU32(reader));

                uint sectionLength = ReadU32(reader);
                if (sectionLength == 0) return document;
                long sectionEnd = stream.Position + sectionLength;
                uint infoLength = ReadU32(reader);
                if (infoLength > 0) document.Layers = ReadLayers(reader);
                stream.Position = sectionEnd;
                return document;
            }
        }

        public static byte[] PackBits(byte[] row, int count)
        {
            using (var output = new MemoryStream(count + count / 128 + 2))
            {
                int i = 0;
                while (i < count)
                {
                    int run = 1;
                    while (i + run < count && run < 128 && row[i + run] == row[i]) run++;
                    if (run >= 3)
                    {
                        output.WriteByte((byte)(257 - run));
                        output.WriteByte(row[i]);
                        i += run;
                        continue;
                    }
                    int start = i;
                    int length = 0;
                    while (i < count && length < 128)
                    {
                        if (i + 2 < count && row[i] == row[i + 1] && row[i] == row[i + 2]) break;
                        i++;
                        length++;
                    }
                    output.WriteByte((byte)(length - 1));
                    output.Write(row, start, length);
                }
                return output.ToArray();
            }
        }

        public static void UnpackBits(byte[] source, int offset, int length, byte[] target, int targetOffset, int count)
        {
            int read = offset;
            int end = offset + length;
            int written = 0;
            while (written < count && read < end)
            {
                var header = (sbyte)source[read++];
                if (header >= 0)
                {
                    int literal = Math.Min(header + 1, count - written);
                    Buffer.BlockCopy(source, read, target, targetOffset + written, literal);
                    read += header + 1;
                    written += literal;
                }
                else if (header != -128)
                {
                    int repeat = Math.Min(1 - header, count - written);
                    byte value = source[read++];
                    for (int k = 0; k < repeat; k++) target[targetOffset + written + k] = value;
                    written += repeat;
                }
            }
        }

        public static RectInt Bounds(byte[] pixels, int width, int height)
        {
            int xMin = width, yMin = height, xMax = -1, yMax = -1;
            for (int y = 0; y < height; y++)
            {
                int row = y * width * 4;
                for (int x = 0; x < width; x++)
                {
                    if (pixels[row + x * 4 + 3] == 0) continue;
                    if (x < xMin) xMin = x;
                    if (x > xMax) xMax = x;
                    if (y < yMin) yMin = y;
                    if (y > yMax) yMax = y;
                }
            }
            return xMax < 0 ? new RectInt(0, 0, 0, 0) : new RectInt(xMin, yMin, xMax - xMin + 1, yMax - yMin + 1);
        }

        public static byte[] Crop(byte[] pixels, int width, RectInt rect)
        {
            var cropped = new byte[rect.width * rect.height * 4];
            for (int y = 0; y < rect.height; y++)
            {
                Buffer.BlockCopy(pixels, ((rect.y + y) * width + rect.x) * 4, cropped, y * rect.width * 4, rect.width * 4);
            }
            return cropped;
        }
        #endregion

        #region [Private Methods]
        private static void Flatten(List<Layer> layers, List<Record> records)
        {
            foreach (var layer in layers)
            {
                if (!layer.IsGroup)
                {
                    records.Add(new Record { Source = layer, Section = 0, Name = layer.Name, Channels = EncodeChannels(layer) });
                    continue;
                }
                records.Add(new Record { Source = layer, Section = 3, Name = GroupEndName, Channels = EmptyChannels() });
                Flatten(layer.Children, records);
                records.Add(new Record { Source = layer, Section = 1, Name = layer.Name, Channels = EmptyChannels() });
            }
        }

        private static byte[] LayerInfo(List<Record> records)
        {
            using (var memory = new MemoryStream())
            {
                var writer = new BinaryWriter(memory);
                WriteU16(writer, (ushort)(short)-records.Count);
                foreach (var record in records)
                {
                    Layer layer = record.Source;
                    RectInt rect = record.Section == 0 ? layer.Rect : new RectInt(0, 0, 0, 0);
                    WriteU32(writer, (uint)rect.y);
                    WriteU32(writer, (uint)rect.x);
                    WriteU32(writer, (uint)(rect.y + rect.height));
                    WriteU32(writer, (uint)(rect.x + rect.width));
                    WriteU16(writer, (ushort)ChannelIds.Length);
                    for (int c = 0; c < ChannelIds.Length; c++)
                    {
                        WriteU16(writer, (ushort)(short)ChannelIds[c]);
                        WriteU32(writer, (uint)record.Channels[c].Length);
                    }
                    writer.Write(Encoding.ASCII.GetBytes("8BIM"));
                    writer.Write(Encoding.ASCII.GetBytes(record.Section == 0 ? Key(layer.BlendMode) : PassBlend));
                    writer.Write(record.Section == 3 ? (byte)255 : layer.Opacity);
                    writer.Write((byte)0);
                    byte flags = record.Section == 0 ? (byte)0 : GroupFlags;
                    if (!layer.Visible && record.Section != 3) flags |= HiddenFlag;
                    writer.Write(flags);
                    writer.Write((byte)0);
                    byte[] extra = ExtraData(record);
                    WriteU32(writer, (uint)extra.Length);
                    writer.Write(extra);
                }
                foreach (var record in records)
                {
                    foreach (var channel in record.Channels) writer.Write(channel);
                }
                while (memory.Length % 4 != 0) writer.Write((byte)0);
                return memory.ToArray();
            }
        }

        private static byte[] ExtraData(Record record)
        {
            using (var memory = new MemoryStream())
            {
                var writer = new BinaryWriter(memory);
                WriteU32(writer, 0);
                WriteU32(writer, 0);
                byte[] ascii = Encoding.ASCII.GetBytes(new string(record.Name.Select(c => c < 128 ? c : '?').Take(255).ToArray()));
                writer.Write((byte)ascii.Length);
                writer.Write(ascii);
                for (int padded = 1 + ascii.Length; padded % 4 != 0; padded++) writer.Write((byte)0);

                var unicode = new List<byte>();
                unicode.AddRange(U32(record.Name.Length));
                foreach (char c in record.Name)
                {
                    unicode.Add((byte)(c >> 8));
                    unicode.Add((byte)c);
                }
                Block(writer, "luni", unicode.ToArray());
                if (record.Section != 0)
                {
                    var section = new List<byte>(U32(record.Section));
                    if (record.Section != 3)
                    {
                        section.AddRange(Encoding.ASCII.GetBytes("8BIM"));
                        section.AddRange(Encoding.ASCII.GetBytes(PassBlend));
                    }
                    Block(writer, "lsct", section.ToArray());
                }
                if (record.Source.Locked && record.Section != 3) Block(writer, "lspf", U32((int)LockedFlags));
                return memory.ToArray();
            }
        }

        private static void Block(BinaryWriter writer, string key, byte[] data)
        {
            int padded = (data.Length + 3) / 4 * 4;
            writer.Write(Encoding.ASCII.GetBytes("8BIM"));
            writer.Write(Encoding.ASCII.GetBytes(key));
            WriteU32(writer, (uint)padded);
            writer.Write(data);
            for (int i = data.Length; i < padded; i++) writer.Write((byte)0);
        }

        private static byte[][] EncodeChannels(Layer layer)
        {
            int width = layer.Rect.width;
            int height = layer.Rect.height;
            if (width * height == 0 || layer.Pixels == null) return EmptyChannels();
            var channels = new byte[ChannelIds.Length][];
            var row = new byte[width];
            for (int c = 0; c < ChannelIds.Length; c++)
            {
                int component = ChannelIds[c] < 0 ? 3 : ChannelIds[c];
                var rows = new byte[height][];
                for (int y = 0; y < height; y++)
                {
                    int start = y * width * 4 + component;
                    for (int x = 0; x < width; x++) row[x] = layer.Pixels[start + x * 4];
                    rows[y] = PackBits(row, width);
                }
                using (var memory = new MemoryStream())
                {
                    var writer = new BinaryWriter(memory);
                    WriteU16(writer, 1);
                    foreach (var packed in rows) WriteU16(writer, (ushort)packed.Length);
                    foreach (var packed in rows) writer.Write(packed);
                    channels[c] = memory.ToArray();
                }
            }
            return channels;
        }

        private static byte[][] EmptyChannels()
        {
            return ChannelIds.Select(id => new byte[2]).ToArray();
        }

        private static void WriteComposite(BinaryWriter writer, byte[] composite, int width, int height)
        {
            WriteU16(writer, 1);
            var row = new byte[width];
            var packed = new byte[4 * height][];
            for (int component = 0; component < 4; component++)
            {
                for (int y = 0; y < height; y++)
                {
                    int start = y * width * 4 + component;
                    for (int x = 0; x < width; x++) row[x] = composite[start + x * 4];
                    packed[component * height + y] = PackBits(row, width);
                }
            }
            foreach (var line in packed) WriteU16(writer, (ushort)line.Length);
            foreach (var line in packed) writer.Write(line);
        }

        private static List<Layer> ReadLayers(BinaryReader reader)
        {
            int count = Math.Abs((short)ReadU16(reader));
            var layers = new List<(Layer Layer, int Section, (int Id, uint Length)[] Channels)>(count);
            for (int i = 0; i < count; i++) layers.Add(ReadRecord(reader));

            foreach (var (layer, section, channels) in layers)
            {
                int width = layer.Rect.width;
                int height = layer.Rect.height;
                bool pixels = section == 0 && width > 0 && height > 0;
                if (pixels) layer.Pixels = new byte[width * height * 4];
                bool hasAlpha = false;
                foreach (var (id, length) in channels)
                {
                    byte[] data = reader.ReadBytes((int)length);
                    if (!pixels || id < -1 || id > 2 || data.Length < 2) continue;
                    hasAlpha |= id == -1;
                    DecodeChannel(data, layer.Pixels, width, height, id < 0 ? 3 : id);
                }
                if (pixels && !hasAlpha)
                {
                    for (int p = 3; p < layer.Pixels.Length; p += 4) layer.Pixels[p] = 255;
                }
            }
            return BuildTree(layers.Select(entry => (entry.Layer, entry.Section)).ToList());
        }

        private static (Layer, int, (int, uint)[]) ReadRecord(BinaryReader reader)
        {
            int top = (int)ReadU32(reader);
            int left = (int)ReadU32(reader);
            int bottom = (int)ReadU32(reader);
            int right = (int)ReadU32(reader);
            int channelCount = ReadU16(reader);
            var channels = new (int, uint)[channelCount];
            for (int c = 0; c < channelCount; c++) channels[c] = ((short)ReadU16(reader), ReadU32(reader));
            reader.ReadBytes(4);
            var layer = new Layer
            {
                Rect = new RectInt(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top)),
                BlendMode = Encoding.ASCII.GetString(reader.ReadBytes(4)),
                Opacity = reader.ReadByte(),
                Clipped = reader.ReadByte() != 0
            };
            byte flags = reader.ReadByte();
            layer.Visible = (flags & HiddenFlag) == 0;
            reader.ReadByte();

            uint extraLength = ReadU32(reader);
            long extraEnd = reader.BaseStream.Position + extraLength;
            uint maskLength = ReadU32(reader);
            layer.HasMask = maskLength > 0;
            Skip(reader, maskLength);
            Skip(reader, ReadU32(reader));
            int nameLength = reader.ReadByte();
            layer.Name = Encoding.ASCII.GetString(reader.ReadBytes(nameLength));
            for (int padded = 1 + nameLength; padded % 4 != 0; padded++) reader.ReadByte();

            int section = 0;
            while (reader.BaseStream.Position + 12 <= extraEnd)
            {
                string signature = Encoding.ASCII.GetString(reader.ReadBytes(4));
                if (signature != "8BIM" && signature != "8B64") break;
                string key = Encoding.ASCII.GetString(reader.ReadBytes(4));
                uint length = ReadU32(reader);
                long blockEnd = reader.BaseStream.Position + length;
                if (key == "luni" && length >= 4)
                {
                    int characters = (int)ReadU32(reader);
                    var name = new StringBuilder(characters);
                    for (int c = 0; c < characters && reader.BaseStream.Position + 2 <= blockEnd; c++) name.Append((char)ReadU16(reader));
                    layer.Name = name.ToString().TrimEnd('\0');
                }
                else if (key == "lsct" && length >= 4)
                {
                    section = (int)ReadU32(reader);
                }
                else if (key == "lspf" && length >= 4)
                {
                    layer.Locked = (ReadU32(reader) & (LockedFlags | 0x80000000)) != 0;
                }
                reader.BaseStream.Position = blockEnd;
            }
            reader.BaseStream.Position = extraEnd;
            return (layer, section, channels);
        }

        private static void DecodeChannel(byte[] data, byte[] pixels, int width, int height, int component)
        {
            int compression = data[0] << 8 | data[1];
            var row = new byte[width];
            if (compression == 0)
            {
                for (int y = 0; y < height; y++)
                {
                    int start = 2 + y * width;
                    if (start + width > data.Length) break;
                    for (int x = 0; x < width; x++) pixels[(y * width + x) * 4 + component] = data[start + x];
                }
                return;
            }
            if (compression != 1) throw new InvalidDataException("A layer uses ZIP compression. Save the PSD with Maximize Compatibility and RLE (8-bit files do this by default).");
            int offset = 2 + height * 2;
            for (int y = 0; y < height; y++)
            {
                int length = data[2 + y * 2] << 8 | data[3 + y * 2];
                UnpackBits(data, offset, Math.Min(length, data.Length - offset), row, 0, width);
                offset += length;
                for (int x = 0; x < width; x++) pixels[(y * width + x) * 4 + component] = row[x];
            }
        }

        private static List<Layer> BuildTree(List<(Layer Layer, int Section)> records)
        {
            var root = new List<Layer>();
            var open = new Stack<List<Layer>>();
            List<Layer> current = root;
            foreach (var (layer, section) in records)
            {
                if (section == 3)
                {
                    open.Push(current);
                    current = new List<Layer>();
                }
                else if ((section == 1 || section == 2) && open.Count > 0)
                {
                    layer.Children = current;
                    layer.Pixels = null;
                    current = open.Pop();
                    current.Add(layer);
                }
                else if (section == 0)
                {
                    current.Add(layer);
                }
            }
            while (open.Count > 0)
            {
                List<Layer> orphans = current;
                current = open.Pop();
                current.AddRange(orphans);
            }
            return root;
        }

        private static string Key(string blendMode)
        {
            return string.IsNullOrEmpty(blendMode) || blendMode.Length != 4 ? NormalBlend : blendMode;
        }

        private static byte[] U32(int value)
        {
            return new[] { (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value };
        }

        private static void WriteU16(BinaryWriter writer, ushort value)
        {
            writer.Write((byte)(value >> 8));
            writer.Write((byte)value);
        }

        private static void WriteU32(BinaryWriter writer, uint value)
        {
            writer.Write((byte)(value >> 24));
            writer.Write((byte)(value >> 16));
            writer.Write((byte)(value >> 8));
            writer.Write((byte)value);
        }

        private static int ReadU16(BinaryReader reader)
        {
            byte[] bytes = reader.ReadBytes(2);
            if (bytes.Length < 2) throw new EndOfStreamException("The PSD file ends too early.");
            return bytes[0] << 8 | bytes[1];
        }

        private static uint ReadU32(BinaryReader reader)
        {
            byte[] bytes = reader.ReadBytes(4);
            if (bytes.Length < 4) throw new EndOfStreamException("The PSD file ends too early.");
            return (uint)(bytes[0] << 24 | bytes[1] << 16 | bytes[2] << 8 | bytes[3]);
        }

        private static void Skip(BinaryReader reader, uint length)
        {
            reader.BaseStream.Position += length;
        }
        #endregion
    }
}
