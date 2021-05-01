﻿using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using static Hack.io.J3D.JUtility;

namespace Hack.io.BTI
{
    public class BTI
    {
        /// <summary>
        /// Filename of this <see cref="BTI"/> file.
        /// </summary>
        public string FileName { get; set; } = null;
        private List<Bitmap> mipmaps = new List<Bitmap>();
        public GXImageFormat Format { get; set; }
        public GXPaletteFormat PaletteFormat { get; set; }
        public JUTTransparency AlphaSetting { get; set; }
        public GXWrapMode WrapS { get; set; }
        public GXWrapMode WrapT { get; set; }
        public GXFilterMode MagnificationFilter { get; set; }
        public GXFilterMode MinificationFilter { get; set; }
        public float MinLOD { get; set; } // Fixed point number, 1/8 = conversion (ToDo: is this multiply by 8 or divide...)
        public float MaxLOD { get; set; } // Fixed point number, 1/8 = conversion (ToDo: is this multiply by 8 or divide...)
        public bool EnableEdgeLOD { get; set; }
        public float LODBias { get; set; } // Fixed point number, 1/100 = conversion
        public bool ClampLODBias { get; set; }
        public byte MaxAnisotropy { get; set; }

        #region Auto Properties
        /// <summary>
        /// The Amount of images inside this BTI. Basically the mipmap count
        /// </summary>
        public int ImageCount => mipmaps == null ? -1 : mipmaps.Count;
        public Bitmap this[int MipmapLevel]
        {
            get => mipmaps[MipmapLevel];
            set
            {
                if (MipmapLevel < 0)
                    throw new ArgumentOutOfRangeException("MipmapLevel");
                if (MipmapLevel == 0)
                    mipmaps[0] = value;
                else
                {
                    int requiredmipwidth = mipmaps[0].Width, requiredmipheight = mipmaps[0].Height;
                    for (int i = 0; i < MipmapLevel; i++)
                    {
                        if (requiredmipwidth == 1 || requiredmipwidth == 1)
                            throw new Exception($"The provided Mipmap Level is too high and will provide image dimensions less than 1x1. Currently, the Max Mipmap Level is {i-1}");
                        requiredmipwidth /= 2;
                        requiredmipheight /= 2;
                    }
                    if (value.Width != requiredmipwidth || value.Height != requiredmipheight)
                        throw new Exception($"The dimensions of the provided mipmap are supposed to be {requiredmipwidth}x{requiredmipheight}");

                    if (MipmapLevel == mipmaps.Count)
                        mipmaps.Add(value);
                    else if (MipmapLevel > mipmaps.Count)
                    {
                        while (mipmaps.Count - 1 != MipmapLevel)
                            mipmaps.Add(new Bitmap(mipmaps[mipmaps.Count - 1], new Size(mipmaps[mipmaps.Count - 1].Width / 2, mipmaps[mipmaps.Count - 1].Height / 2)));
                        mipmaps.Add(value);
                    }
                    else
                        mipmaps[MipmapLevel] = value;
                }
            }
        }
        #endregion

        internal BTI() { }
        public BTI(string filename)
        {
            FileStream BTIFile = new FileStream(filename, FileMode.Open);
            Read(BTIFile);
            BTIFile.Close();
            FileName = filename;
        }
        public BTI(Stream memorystream) => Read(memorystream);

        public void Save(string filename)
        {
            FileStream BTIFile = new FileStream(filename, FileMode.Create);
            long BaseDataOffset = 0x20;
            Write(BTIFile, ref BaseDataOffset);
            BTIFile.Close();
        }
        public void Save(Stream BTIFile, ref long DataOffset) => Write(BTIFile, ref DataOffset);

        private void Read(Stream BTIFile)
        {
            long HeaderStart = BTIFile.Position;
            Format = (GXImageFormat)BTIFile.ReadByte();
            AlphaSetting = (JUTTransparency)BTIFile.ReadByte();
            ushort ImageWidth = BitConverter.ToUInt16(BTIFile.ReadReverse(0, 2), 0);
            ushort ImageHeight = BitConverter.ToUInt16(BTIFile.ReadReverse(0, 2), 0);
            WrapS = (GXWrapMode)BTIFile.ReadByte();
            WrapT = (GXWrapMode)BTIFile.ReadByte();
            bool UsePalettes = BTIFile.ReadByte() > 0;
            short PaletteCount = 0;
            uint PaletteDataAddress = 0;
            byte[] PaletteData = null;
            if (UsePalettes)
            {
                PaletteFormat = (GXPaletteFormat)BTIFile.ReadByte();
                PaletteCount = BitConverter.ToInt16(BTIFile.ReadReverse(0, 2), 0);
                PaletteDataAddress = BitConverter.ToUInt32(BTIFile.ReadReverse(0, 4), 0);
                long PausePosition = BTIFile.Position;
                BTIFile.Position = HeaderStart + PaletteDataAddress;
                PaletteData = BTIFile.Read(0, PaletteCount * 2);
                BTIFile.Position = PausePosition;
            }
            else
                BTIFile.Position += 7;
            bool EnableMipmaps = BTIFile.ReadByte() > 0;
            EnableEdgeLOD = BTIFile.ReadByte() > 0;
            ClampLODBias = BTIFile.ReadByte() > 0;
            MaxAnisotropy = (byte)BTIFile.ReadByte();
            MinificationFilter = (GXFilterMode)BTIFile.ReadByte();
            MagnificationFilter = (GXFilterMode)BTIFile.ReadByte();
            MinLOD = ((sbyte)BTIFile.ReadByte() / 8.0f);
            MaxLOD = ((sbyte)BTIFile.ReadByte() / 8.0f);

            byte TotalImageCount = (byte)BTIFile.ReadByte();
            BTIFile.Position++;
            LODBias = BitConverter.ToInt16(BTIFile.ReadReverse(0, 2), 0) / 100.0f;
            uint ImageDataAddress = BitConverter.ToUInt32(BTIFile.ReadReverse(0, 4), 0);

            BTIFile.Position = HeaderStart + ImageDataAddress;
            ushort ogwidth = ImageWidth, ogheight = ImageHeight;
            for (int i = 0; i < TotalImageCount; i++)
            {
                if (i > 0)
                {
                    ImageWidth = (ushort)(ImageWidth / 2);
                    ImageHeight = (ushort)(ImageHeight / 2);
                }
                byte[] ImageData = null;
                Bitmap Result = null;
                int oldWidth = ImageWidth % 4 != 0 ? (ImageWidth / 4) * 4 + 4 : ImageWidth;
                int FullWidth = oldWidth % 8 != 0 ? (oldWidth / 8) * 8 + 8 : oldWidth;

                int oldHeight = ImageHeight % 4 != 0 ? (ImageHeight / 4) * 4 + 4 : ImageHeight;
                int FullHeight = oldHeight % 8 != 0 ? (oldHeight / 8) * 8 + 8 : oldHeight;
                switch (Format)
                {
                    case GXImageFormat.I4:
                        #region DecodeI4
                        ImageData = BTIFile.Read(0, (ImageWidth * ImageHeight) / 2);
                        Result = DecodeImage(ImageData, null, Format, null, null, ImageWidth, ImageHeight);
                        #endregion
                        break;
                    case GXImageFormat.I8:
                        #region DecodeI8
                        ImageData = BTIFile.Read(0, ImageWidth * ImageHeight);
                        Result = DecodeImage(ImageData, null, Format, null, null, ImageWidth, ImageHeight);
                        #endregion
                        break;
                    case GXImageFormat.IA4:
                        #region DecodeIA4
                        ImageData = BTIFile.Read(0, ImageWidth * ImageHeight);
                        Result = DecodeImage(ImageData, null, Format, null, null, ImageWidth, ImageHeight);
                        #endregion
                        break;
                    case GXImageFormat.IA8:
                        #region DecodeIA8
                        ImageData = BTIFile.Read(0, (ImageWidth * ImageHeight) * 2);
                        Result = DecodeImage(ImageData, null, Format, null, null, ImageWidth, ImageHeight);
                        #endregion
                        break;
                    case GXImageFormat.RGB565:
                        #region DecodeRGB565
                        ImageData = BTIFile.Read(0, (ImageWidth * ImageHeight) * 2);
                        Result = DecodeImage(ImageData, null, Format, null, null, ImageWidth, ImageHeight);
                        #endregion
                        break;
                    case GXImageFormat.RGB5A3:
                        #region DecodeRGB5A3
                        ImageData = BTIFile.Read(0, (ImageWidth * ImageHeight) * 2);
                        Result = DecodeImage(ImageData, null, Format, null, null, ImageWidth, ImageHeight);
                        #endregion
                        break;
                    case GXImageFormat.RGBA32:
                        #region DecodeRGBA32
                        ImageData = BTIFile.Read(0, (ImageWidth * ImageHeight) * 4);
                        Result = DecodeImage(ImageData, null, Format, null, null, ImageWidth, ImageHeight);
                        #endregion
                        break;
                    case GXImageFormat.C4:
                        #region DecodeC4
                        ImageData = BTIFile.Read(0, (FullWidth * FullHeight) / 2);
                        Result = DecodeImage(ImageData, PaletteData, Format, PaletteFormat, PaletteCount, FullWidth, FullHeight);
                        #endregion
                        break;
                    case GXImageFormat.C8:
                        #region DecodeC8
                        ImageData = BTIFile.Read(0, FullWidth * FullHeight);
                        Result = DecodeImage(ImageData, PaletteData, Format, PaletteFormat, PaletteCount, FullWidth, FullHeight);
                        #endregion
                        break;
                    case GXImageFormat.C14X2:
                        #region DecodeC14X2
                        ImageData = BTIFile.Read(0, (FullWidth * FullHeight) * 2);
                        Result = DecodeImage(ImageData, PaletteData, Format, PaletteFormat, PaletteCount, FullWidth, FullHeight);
                        #endregion
                        break;
                    case GXImageFormat.CMPR:
                        #region DecodeDXT1
                        int BytesNeededForEncode = FullWidth * FullHeight * 4 / 8;
                        ImageData = BTIFile.Read(0, BytesNeededForEncode);
                        Result = DecodeImage(ImageData, null, Format, null, null, FullWidth, FullHeight);
                        #endregion
                        break;
                    default:
                        throw new Exception("Invalid format");
                }
                Bitmap nb = new Bitmap(ImageWidth, ImageHeight);
                using (Graphics g = Graphics.FromImage(nb))
                {
                    g.DrawImage(Result, 0, 0);
                }
                Result.Dispose();
                mipmaps.Add(nb);
            }
        }
        private void Write(Stream BTIFile, ref long DataOffset)
        {
            List<byte> ImageData = new List<byte>();
            List<byte> PaletteData = new List<byte>();
            GetImageAndPaletteData(ref ImageData, ref PaletteData, mipmaps, Format, PaletteFormat);
            long HeaderStart = BTIFile.Position;
            int ImageDataStart = (int)((DataOffset + PaletteData.Count) - HeaderStart), PaletteDataStart = (int)(DataOffset - HeaderStart);
            BTIFile.WriteByte((byte)Format);
            BTIFile.WriteByte((byte)AlphaSetting);
            BTIFile.WriteReverse(BitConverter.GetBytes((ushort)mipmaps[0].Width), 0, 2);
            BTIFile.WriteReverse(BitConverter.GetBytes((ushort)mipmaps[0].Height), 0, 2);
            BTIFile.WriteByte((byte)WrapS);
            BTIFile.WriteByte((byte)WrapT);
            if (Format.IsPaletteFormat())
            {
                BTIFile.WriteByte(0x01);
                BTIFile.WriteByte((byte)PaletteFormat);
                BTIFile.WriteReverse(BitConverter.GetBytes((ushort)(PaletteData.Count/2)), 0, 2);
                BTIFile.WriteReverse(BitConverter.GetBytes(PaletteDataStart), 0, 4);
            }
            else
                BTIFile.Write(new byte[8], 0, 8);

            BTIFile.WriteByte((byte)(mipmaps.Count > 1 ? 0x01 : 0x00));
            BTIFile.WriteByte((byte)(EnableEdgeLOD ? 0x01 : 0x00));
            BTIFile.WriteByte((byte)(ClampLODBias ? 0x01 : 0x00));
            BTIFile.WriteByte(MaxAnisotropy);
            BTIFile.WriteByte((byte)MinificationFilter);
            BTIFile.WriteByte((byte)MagnificationFilter);
            BTIFile.WriteByte((byte)(MinLOD * 8));
            BTIFile.WriteByte((byte)(MaxLOD * 8));
            BTIFile.WriteByte((byte)mipmaps.Count);
            BTIFile.WriteByte(0x00);
            BTIFile.WriteReverse(BitConverter.GetBytes((short)(LODBias * 100)), 0, 2);
            BTIFile.WriteReverse(BitConverter.GetBytes(ImageDataStart), 0, 4);

            long Pauseposition = BTIFile.Position;
            BTIFile.Position = DataOffset;

            BTIFile.Write(PaletteData.ToArray(), 0, PaletteData.Count);
            BTIFile.Write(ImageData.ToArray(), 0, ImageData.Count);
            DataOffset = BTIFile.Position;
            BTIFile.Position = Pauseposition;
        }
        
        public bool ImageEquals(BTI Other)
        {
            if (mipmaps.Count != Other.mipmaps.Count)
                return false;
            for (int i = 0; i < mipmaps.Count; i++)
            {
                if (!Compare(mipmaps[i], Other.mipmaps[i]))
                    return false;
            }

            return true;
        }
        public override string ToString() => $"{FileName} - {mipmaps.Count} Image(s)";
        public override bool Equals(object obj)
        {
            return obj is BTI bTI && bTI != null &&
                   FileName == bTI.FileName &&
                   ImageEquals(bTI) &&
                   Format == bTI.Format &&
                   PaletteFormat == bTI.PaletteFormat &&
                   AlphaSetting == bTI.AlphaSetting &&
                   WrapS == bTI.WrapS &&
                   WrapT == bTI.WrapT &&
                   MagnificationFilter == bTI.MagnificationFilter &&
                   MinificationFilter == bTI.MinificationFilter &&
                   MinLOD == bTI.MinLOD &&
                   MaxLOD == bTI.MaxLOD &&
                   EnableEdgeLOD == bTI.EnableEdgeLOD &&
                   LODBias == bTI.LODBias &&
                   ClampLODBias == bTI.ClampLODBias &&
                   MaxAnisotropy == bTI.MaxAnisotropy &&
                   ImageCount == bTI.ImageCount;
        }
        public override int GetHashCode()
        {
            var hashCode = 647188357;
            hashCode = hashCode * -1521134295 + EqualityComparer<string>.Default.GetHashCode(FileName);
            hashCode = hashCode * -1521134295 + EqualityComparer<List<Bitmap>>.Default.GetHashCode(mipmaps);
            hashCode = hashCode * -1521134295 + Format.GetHashCode();
            hashCode = hashCode * -1521134295 + PaletteFormat.GetHashCode();
            hashCode = hashCode * -1521134295 + AlphaSetting.GetHashCode();
            hashCode = hashCode * -1521134295 + WrapS.GetHashCode();
            hashCode = hashCode * -1521134295 + WrapT.GetHashCode();
            hashCode = hashCode * -1521134295 + MagnificationFilter.GetHashCode();
            hashCode = hashCode * -1521134295 + MinificationFilter.GetHashCode();
            hashCode = hashCode * -1521134295 + MinLOD.GetHashCode();
            hashCode = hashCode * -1521134295 + MaxLOD.GetHashCode();
            hashCode = hashCode * -1521134295 + EnableEdgeLOD.GetHashCode();
            hashCode = hashCode * -1521134295 + LODBias.GetHashCode();
            hashCode = hashCode * -1521134295 + ClampLODBias.GetHashCode();
            hashCode = hashCode * -1521134295 + MaxAnisotropy.GetHashCode();
            hashCode = hashCode * -1521134295 + ImageCount.GetHashCode();
            return hashCode;
        }
        public static bool operator ==(BTI bTI1, BTI bTI2) => bTI1.Equals(bTI2);
        public static bool operator !=(BTI bTI1, BTI bTI2) => !(bTI1 == bTI2);

        //=====================================================================

        /// <summary>
        /// Cast a RARCFile to a BTI
        /// </summary>
        /// <param name="x"></param>
        public static implicit operator BTI(RARC.RARC.File x) => new BTI((MemoryStream)x) { FileName = x.Name };
        /// <summary>
        /// Cast a BTI to a RARCfile
        /// </summary>
        /// <param name="x"></param>
        public static implicit operator RARC.RARC.File(BTI x)
        {
            MemoryStream MS = new MemoryStream();
            long temp = 0;
            x.Write(MS, ref temp);
            return new RARC.RARC.File(x.FileName, MS);
        }
        /// <summary>
        /// Cast a Bitmap to a BTI
        /// </summary>
        /// <param name="Source"></param>
        public static explicit operator BTI(Bitmap Source)
        {
            BTI NewImage = new BTI { Format = GXImageFormat.CMPR };
            NewImage.mipmaps.Add(Source);
            return NewImage;
        }
        /// <summary>
        /// Cast Bitmaps to a BTI
        /// </summary>
        /// <param name="Source"></param>
        public static explicit operator BTI(Bitmap[] Source)
        {
            BTI NewImage = new BTI { Format = GXImageFormat.CMPR };
            NewImage.mipmaps.Add(Source[0]);
            for (int i = 1; i < Source.Length; i++)
            {
                if (Source[i].Width < 1 || Source[i].Height < 1)
                    break;
                if (Source[i].Width == Source[i - 1].Width / 2 && Source[i].Height == Source[i - 1].Height / 2)
                    NewImage.mipmaps.Add(Source[i]);
            }
            return NewImage;
        }

        //=====================================================================
    }
}