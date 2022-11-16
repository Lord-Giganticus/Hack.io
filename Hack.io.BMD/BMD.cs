using System;
using System.IO;
using static Hack.io.J3D.JUtility;

//Heavily based on the SuperBMD Library.
namespace Hack.io.BMD
{
    public partial class BMD
    {
        public string FileName { get; set; }
        public INF1 Scenegraph { get; protected set; }
        public VTX1 VertexData { get; protected set; }
        public EVP1 SkinningEnvelopes { get; protected set; }
        public DRW1 PartialWeightData { get; protected set; }
        public JNT1 Joints { get; protected set; }
        public SHP1 Shapes { get; protected set; }
        public MAT3 Materials { get; protected set; }
        public TEX1 Textures { get; protected set; }

        private static readonly string Magic = "J3D2bmd3";

        public BMD() { }
        public BMD(string Filename)
        {
            FileStream FS = new(Filename, FileMode.Open);
            Read(FS);
            FS.Close();
            FileName = Filename;
        }
        public BMD(Stream BMD) => Read(BMD);

        public static bool CheckFile(string Filename)
        {
            FileStream FS = new(Filename, FileMode.Open);
            bool result = FS.ReadString(8).Equals(Magic);
            FS.Close();
            return result;
        }
        public static bool CheckFile(Stream BMD) => BMD.ReadString(8).Equals(Magic);

        public virtual void Save(string Filename)
        {
            FileStream FS = new(Filename, FileMode.Create);
            Write(FS);
            FS.Close();
            FileName = Filename;
        }
        public virtual void Save(Stream BMD) => Write(BMD);

        protected virtual void Read(Stream BMD)
        {
            if (!BMD.ReadString(8).Equals(Magic))
                throw new Exception($"Invalid Identifier. Expected \"{Magic}\"");

            BMD.Position += 0x08+16;
            Scenegraph = new INF1(BMD, out int VertexCount);
            VertexData = new VTX1(BMD, VertexCount);
            SkinningEnvelopes = new EVP1(BMD);
            PartialWeightData = new DRW1(BMD);
            Joints = new JNT1(BMD);
            Shapes = new SHP1(BMD);
            SkinningEnvelopes.SetInverseBindMatrices(Joints.FlatSkeleton);
            Shapes.SetVertexWeights(SkinningEnvelopes, PartialWeightData);
            Joints.InitBoneFamilies(Scenegraph);
            Joints.InitBoneMatricies(Scenegraph);
            Materials = new MAT3(BMD);
            if (BitConverter.ToInt32(BMD.ReadReverse(0, 4), 0) == 0x4D444C33)
            {
                int mdl3Size = BitConverter.ToInt32(BMD.ReadReverse(0, 4), 0);
                BMD.Position += mdl3Size-0x08;
            }
            else
                BMD.Position -= 0x04;
            Textures = new TEX1(BMD);
            Materials.SetTextureNames(Textures);
            //VertexData.StipUnused(Shapes);
        }

        protected virtual void Write(Stream BMD)
        {
            BMD.WriteString(Magic);
            bool IsBDL = false;
            BMD.Write(new byte[8] { 0xDD, 0xDD, 0xDD, 0xDD, 0x00, 0x00, 0x00, (byte)(IsBDL ? 0x09 : 0x08) }, 0, 8);
            BMD.Write(new byte[16], 0, 16);

            Scenegraph.Write(BMD, Shapes, VertexData);
            VertexData.Write(BMD);
            SkinningEnvelopes.Write(BMD);
            PartialWeightData.Write(BMD);
            Joints.Write(BMD);
            Shapes.Write(BMD);
            Textures.UpdateTextures(Materials);
            Materials.Write(BMD);
            Textures.Write(BMD);

            BMD.Position = 0x08;
            BMD.WriteReverse(BitConverter.GetBytes((int)BMD.Length), 0, 4);
        }

        public enum GXVertexAttribute
        {
            PositionMatrixIdx = 0,
            Tex0Mtx = 1,
            Tex1Mtx = 2,
            Tex2Mtx = 3,
            Tex3Mtx = 4,
            Tex4Mtx = 5,
            Tex5Mtx = 6,
            Tex6Mtx = 7,
            Tex7Mtx = 8,
            Position = 9,
            Normal = 10,
            Color0 = 11,
            Color1 = 12,
            Tex0 = 13,
            Tex1 = 14,
            Tex2 = 15,
            Tex3 = 16,
            Tex4 = 17,
            Tex5 = 18,
            Tex6 = 19,
            Tex7 = 20,
            PositionMatrixArray = 21,
            NormalMatrixArray = 22,
            TextureMatrixArray = 23,
            LitMatrixArra = 24,
            NormalBinormalTangent = 25,
            MaxAttr = 26,
            Null = 255
        }
        public enum GXDataType
        {
            RGB565 = 0x0,
            RGB8 = 0x1,
            RGBX8 = 0x2,
            RGBA4 = 0x3,
            RGBA6 = 0x4,
            RGBA8 = 0x5
        }
        public enum GXComponentCount
        {
            Position_XY = 0,
            Position_XYZ,

            Normal_XYZ = 2,
            Normal_NBT,
            Normal_NBT3,

            Color_RGB = 5,
            Color_RGBA,

            TexCoord_S = 7,
            TexCoord_ST
        }
        public enum Vtx1OffsetIndex
        {
            PositionData,
            NormalData,
            NBTData,
            Color0Data,
            Color1Data,
            TexCoord0Data,
            TexCoord1Data,
            TexCoord2Data,
            TexCoord3Data,
            TexCoord4Data,
            TexCoord5Data,
            TexCoord6Data,
            TexCoord7Data,
        }
        /// <summary>
        /// Determines how the position and normal matrices are calculated for a shape
        /// </summary>
        public enum DisplayFlags
        {
            /// <summary>
            /// Use a Single Matrix
            /// </summary>
            SingleMatrix,
            /// <summary>
            /// Billboard along all axis
            /// </summary>
            Billboard,
            /// <summary>
            /// Billboard Only along the Y axis
            /// </summary>
            BillboardY,
            /// <summary>
            /// Use Multiple Matrixies (Skinned models)
            /// </summary>
            MultiMatrix
        }
        public enum VertexInputType
        {
            None,
            Direct,
            Index8,
            Index16
        }
        public enum GXPrimitiveType
        {
            Points = 0xB8,
            Lines = 0xA8,
            LineStrip = 0xB0,
            Triangles = 0x90,
            TriangleStrip = 0x98,
            TriangleFan = 0xA0,
            Quads = 0x80,
        }
        public static OpenTK.Graphics.OpenGL.PrimitiveType FromGXToOpenTK(GXPrimitiveType Type)
        {
            return Type switch
            {
                GXPrimitiveType.Points => OpenTK.Graphics.OpenGL.PrimitiveType.Points,
                GXPrimitiveType.Lines => OpenTK.Graphics.OpenGL.PrimitiveType.Lines,
                GXPrimitiveType.LineStrip => OpenTK.Graphics.OpenGL.PrimitiveType.LineStrip,
                GXPrimitiveType.Triangles => OpenTK.Graphics.OpenGL.PrimitiveType.Triangles,
                GXPrimitiveType.TriangleStrip => OpenTK.Graphics.OpenGL.PrimitiveType.TriangleStrip,
                GXPrimitiveType.TriangleFan => OpenTK.Graphics.OpenGL.PrimitiveType.TriangleFan,
                GXPrimitiveType.Quads => OpenTK.Graphics.OpenGL.PrimitiveType.Quads,
                _ => throw new Exception("Bruh moment!!"),
            };
        }
        public static OpenTK.Graphics.OpenGL.TextureWrapMode FromGXToOpenTK(GXWrapMode Type)
        {
            return Type switch
            {
                GXWrapMode.CLAMP => OpenTK.Graphics.OpenGL.TextureWrapMode.Clamp,
                GXWrapMode.REPEAT => OpenTK.Graphics.OpenGL.TextureWrapMode.Repeat,
                GXWrapMode.MIRRORREAPEAT => OpenTK.Graphics.OpenGL.TextureWrapMode.MirroredRepeat,
                _ => throw new Exception("Bruh moment!!"),
            };
        }
        public static OpenTK.Graphics.OpenGL.TextureMinFilter FromGXToOpenTK_Min(GXFilterMode Type)
        {
            return Type switch
            {
                GXFilterMode.Nearest => OpenTK.Graphics.OpenGL.TextureMinFilter.Nearest,
                GXFilterMode.Linear => OpenTK.Graphics.OpenGL.TextureMinFilter.Linear,
                GXFilterMode.NearestMipmapNearest => OpenTK.Graphics.OpenGL.TextureMinFilter.NearestMipmapNearest,
                GXFilterMode.NearestMipmapLinear => OpenTK.Graphics.OpenGL.TextureMinFilter.NearestMipmapLinear,
                GXFilterMode.LinearMipmapNearest => OpenTK.Graphics.OpenGL.TextureMinFilter.LinearMipmapNearest,
                GXFilterMode.LinearMipmapLinear => OpenTK.Graphics.OpenGL.TextureMinFilter.LinearMipmapLinear,
                _ => throw new Exception("Bruh moment!!"),
            };
        }
        public static OpenTK.Graphics.OpenGL.TextureMagFilter FromGXToOpenTK_Mag(GXFilterMode Type)
        {
            switch (Type)
            {
                case GXFilterMode.Nearest:
                    return OpenTK.Graphics.OpenGL.TextureMagFilter.Nearest;
                case GXFilterMode.Linear:
                    return OpenTK.Graphics.OpenGL.TextureMagFilter.Linear;
                case GXFilterMode.NearestMipmapNearest:
                case GXFilterMode.NearestMipmapLinear:
                case GXFilterMode.LinearMipmapNearest:
                case GXFilterMode.LinearMipmapLinear:
                    break;
            }
            throw new Exception("Bruh moment!!");
        }
        public static OpenTK.Graphics.OpenGL.CullFaceMode? FromGXToOpenTK(MAT3.CullMode Type)
        {
            return Type switch
            {
                MAT3.CullMode.None => null,
                MAT3.CullMode.Front => (OpenTK.Graphics.OpenGL.CullFaceMode?)OpenTK.Graphics.OpenGL.CullFaceMode.Back,
                MAT3.CullMode.Back => (OpenTK.Graphics.OpenGL.CullFaceMode?)OpenTK.Graphics.OpenGL.CullFaceMode.Front,
                MAT3.CullMode.All => (OpenTK.Graphics.OpenGL.CullFaceMode?)OpenTK.Graphics.OpenGL.CullFaceMode.FrontAndBack,
                _ => throw new Exception("Bruh moment!!"),
            };
        }
        public static OpenTK.Graphics.OpenGL.BlendingFactor FromGXToOpenTK(MAT3.Material.BlendMode.BlendModeControl Factor)
        {
            switch (Factor)
            {
                case MAT3.Material.BlendMode.BlendModeControl.Zero:
                    return OpenTK.Graphics.OpenGL.BlendingFactor.Zero;
                case MAT3.Material.BlendMode.BlendModeControl.One:
                    return OpenTK.Graphics.OpenGL.BlendingFactor.One;
                case MAT3.Material.BlendMode.BlendModeControl.SrcColor:
                    return OpenTK.Graphics.OpenGL.BlendingFactor.SrcColor;
                case MAT3.Material.BlendMode.BlendModeControl.InverseSrcColor:
                    return OpenTK.Graphics.OpenGL.BlendingFactor.OneMinusSrcColor;
                case MAT3.Material.BlendMode.BlendModeControl.SrcAlpha:
                    return OpenTK.Graphics.OpenGL.BlendingFactor.SrcAlpha;
                case MAT3.Material.BlendMode.BlendModeControl.InverseSrcAlpha:
                    return OpenTK.Graphics.OpenGL.BlendingFactor.OneMinusSrcAlpha;
                case MAT3.Material.BlendMode.BlendModeControl.DstAlpha:
                    return OpenTK.Graphics.OpenGL.BlendingFactor.DstAlpha;
                case MAT3.Material.BlendMode.BlendModeControl.InverseDstAlpha:
                    return OpenTK.Graphics.OpenGL.BlendingFactor.OneMinusDstAlpha;
                default:
                    Console.WriteLine("Unsupported BlendModeControl: \"{0}\" in FromGXToOpenTK!", Factor);
                    return OpenTK.Graphics.OpenGL.BlendingFactor.SrcAlpha;

            }
        }
        public static OpenTK.Graphics.OpenGL.PixelInternalFormat FromGXToOpenTK_InternalFormat(GXImageFormat imageformat)
        {
            return imageformat switch
            {
                GXImageFormat.I4 or GXImageFormat.I8 => OpenTK.Graphics.OpenGL.PixelInternalFormat.Intensity,
                GXImageFormat.IA4 or GXImageFormat.IA8 => OpenTK.Graphics.OpenGL.PixelInternalFormat.Luminance8Alpha8,
                _ => OpenTK.Graphics.OpenGL.PixelInternalFormat.Four,
            };
        }
        public static OpenTK.Graphics.OpenGL.PixelFormat FromGXToOpenTK_PixelFormat(GXImageFormat imageformat)
        {
            return imageformat switch
            {
                GXImageFormat.I4 or GXImageFormat.I8 => OpenTK.Graphics.OpenGL.PixelFormat.Luminance,
                GXImageFormat.IA4 or GXImageFormat.IA8 => OpenTK.Graphics.OpenGL.PixelFormat.LuminanceAlpha,
                _ => OpenTK.Graphics.OpenGL.PixelFormat.Bgra,
            };
        }

        //=====================================================================

        /// <summary>
        /// Cast a RARCFile to a BMD
        /// </summary>
        /// <param name="x"></param>
        public static implicit operator BMD(RARC.RARC.File x) => new((MemoryStream)x) { FileName = x.Name };

        //=====================================================================
    }
}
