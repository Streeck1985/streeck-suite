using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using Windows.AI.MachineLearning;

namespace StreeckSuite.Core
{
    /// <summary>
    /// Modelo de visión ONNX ejecutado por Windows ML, el motor de IA integrado en Windows: no requiere instalar nada
    /// más que el archivo del modelo, que se descarga la primera vez. Entrada RGB normalizada con la media y desviación
    /// de ImageNet; salida de un canal normalizada a 0..1.
    /// </summary>
    public class VisionModel
    {
        /// <summary>
        /// Profundidad relativa (Depth Anything V2 small, ~95 MB): 1 = lo más cercano.
        /// </summary>
        public static readonly VisionModel Depth = new VisionModel(
            "depth_anything_v2_small.onnx",
            "https://huggingface.co/onnx-community/depth-anything-v2-small/resolve/main/onnx/model.onnx",
            518); // Múltiplo de 14, el tamaño de parche de su red ViT

        /// <summary>
        /// Sujeto principal de la imagen (U²-Net pequeño de rembg, ~4,4 MB): 1 = parte del sujeto.
        /// </summary>
        public static readonly VisionModel Subject = new VisionModel(
            "u2netp.onnx",
            "https://github.com/danielgatis/rembg/releases/download/v0.0.0/u2netp.onnx",
            320);

        private static readonly float[] Mean = { 0.485f, 0.456f, 0.406f };
        private static readonly float[] Std = { 0.229f, 0.224f, 0.225f };

        private readonly string _fileName;
        private readonly string _url;

        private VisionModel(string fileName, string url, int size)
        {
            _fileName = fileName;
            _url = url;
            Size = size;
        }

        /// <summary>
        /// Lado de la cuadrícula cuadrada de entrada y de salida.
        /// </summary>
        public int Size { get; private set; }

        public string ModelPath
        {
            get { return Path.Combine(Path.GetDirectoryName(Log.FilePath), "models", _fileName); }
        }

        public bool IsAvailable
        {
            get { return File.Exists(ModelPath); }
        }

        /// <summary>
        /// Descarga el modelo si aún no está. Bloquea: llamar desde un hilo en segundo plano.
        /// </summary>
        public void EnsureDownloaded()
        {
            if (IsAvailable) return;

            string path = ModelPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string tempPath = path + ".download";

            // .NET Framework 4 no activa TLS 1.2 por defecto y GitHub / Hugging Face lo exigen
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
            using (WebClient client = new WebClient())
            {
                client.DownloadFile(_url, tempPath);
            }
            if (File.Exists(path)) File.Delete(path);
            File.Move(tempPath, path);
            Log.Info("[VisionModel] Modelo descargado en {0}", path);
        }

        /// <summary>
        /// Ejecuta el modelo sobre la imagen y devuelve su salida normalizada a 0..1 en una cuadrícula Size x Size.
        /// </summary>
        public float[] Run(Bitmap image)
        {
            float[] input = ToInputTensor(image);

            // CPU: para una sola imagen es más rápido que preparar la GPU (~0,5 s frente a ~1,8 s)
            using (LearningModel model = LearningModel.LoadFromFilePath(ModelPath))
            using (LearningModelSession session = new LearningModelSession(model, new LearningModelDevice(LearningModelDeviceKind.Cpu)))
            {
                LearningModelBinding binding = new LearningModelBinding(session);
                binding.Bind(model.InputFeatures[0].Name, TensorFloat.CreateFromArray(new long[] { 1, 3, Size, Size }, input));
                LearningModelEvaluationResult result = session.Evaluate(binding, _fileName);
                IReadOnlyList<float> raw = ((TensorFloat)result.Outputs[model.OutputFeatures[0].Name]).GetAsVectorView();
                return Normalize(raw);
            }
        }

        private float[] ToInputTensor(Bitmap image)
        {
            float[] input = new float[3 * Size * Size];
            using (Bitmap small = new Bitmap(Size, Size, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(small))
                {
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;
                    g.DrawImage(image, new Rectangle(0, 0, Size, Size));
                }

                int[] pixels = ReadPixels(small);
                int plane = Size * Size;
                for (int i = 0; i < plane; i++)
                {
                    int p = pixels[i];
                    input[i] = (((p >> 16) & 0xFF) / 255f - Mean[0]) / Std[0];
                    input[plane + i] = (((p >> 8) & 0xFF) / 255f - Mean[1]) / Std[1];
                    input[2 * plane + i] = ((p & 0xFF) / 255f - Mean[2]) / Std[2];
                }
            }
            return input;
        }

        private static float[] Normalize(IReadOnlyList<float> raw)
        {
            float min = float.MaxValue, max = float.MinValue;
            foreach (float v in raw)
            {
                if (v < min) min = v;
                if (v > max) max = v;
            }
            float range = Math.Max(max - min, 1e-6f);

            float[] values = new float[raw.Count];
            for (int i = 0; i < values.Length; i++)
            {
                values[i] = (raw[i] - min) / range;
            }
            return values;
        }

        /// <summary>
        /// Lee los píxeles de un bitmap de 32 bpp como enteros ARGB.
        /// </summary>
        public static int[] ReadPixels(Bitmap bitmap)
        {
            Rectangle rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
            BitmapData data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                int[] pixels = new int[bitmap.Width * bitmap.Height];
                Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
                return pixels;
            }
            finally
            {
                bitmap.UnlockBits(data);
            }
        }

        public static void WritePixels(Bitmap bitmap, int[] pixels)
        {
            Rectangle rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
            BitmapData data = bitmap.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                Marshal.Copy(pixels, 0, data.Scan0, pixels.Length);
            }
            finally
            {
                bitmap.UnlockBits(data);
            }
        }
    }
}
