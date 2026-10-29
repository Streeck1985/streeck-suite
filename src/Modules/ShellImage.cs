using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using StreeckSuite.Core;

namespace StreeckSuite.Modules
{
    /// <summary>
    /// Imágenes y elementos del shell de Windows: miniatura o icono de cualquier archivo o app (lo mismo que enseña el
    /// Explorador) y la lista de apps del menú Inicio (carpeta virtual shell:AppsFolder, tienda y escritorio).
    /// </summary>
    internal static class ShellImage
    {
        public class App
        {
            public string Name;
            public string Id;   // AUMID: se abre con shell:AppsFolder\Id
        }

        [ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItem
        {
            [PreserveSig] int BindToHandler(IntPtr bindContext, ref Guid handler, ref Guid riid, out IntPtr result);
            [PreserveSig] int GetParent(out IShellItem parent);
            [PreserveSig] int GetDisplayName(uint sigdn, out IntPtr name);
            [PreserveSig] int GetAttributes(uint mask, out uint attributes);
            [PreserveSig] int Compare(IShellItem other, uint hint, out int order);
        }

        [ComImport, Guid("70629033-e363-4a28-a567-0db78006e6d7"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IEnumShellItems
        {
            [PreserveSig] int Next(uint count, [MarshalAs(UnmanagedType.Interface)] out IShellItem item, out uint fetched);
            [PreserveSig] int Skip(uint count);
            [PreserveSig] int Reset();
            [PreserveSig] int Clone(out IEnumShellItems copy);
        }

        [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItemImageFactory
        {
            [PreserveSig] int GetImage(NativeMethods.SIZE size, int flags, out IntPtr hbitmap);
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        private static extern void SHCreateItemFromParsingName(string path, IntPtr bindContext,
            [MarshalAs(UnmanagedType.LPStruct)] Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellItem item);

        private static readonly Guid BHID_EnumItems = new Guid("94f60519-2850-4924-aa5a-d15e84868039");
        private const uint SIGDN_NORMALDISPLAY = 0;
        private const uint SIGDN_PARENTRELATIVEPARSING = 0x80018001;
        public const int IconOnly = 0x4;       // SIIGBF_ICONONLY
        public const int ThumbnailOrIcon = 0;  // SIIGBF_RESIZETOFIT: miniatura si la hay (fotos, PDF...), si no el icono

        /// <summary>
        /// Imagen de un archivo, carpeta o "shell:AppsFolder\AUMID" con transparencia; null si no se puede obtener.
        /// </summary>
        public static Bitmap Get(string parsingName, int size, int flags)
        {
            IShellItem item = null;
            try
            {
                SHCreateItemFromParsingName(parsingName, IntPtr.Zero, typeof(IShellItem).GUID, out item);
                IShellItemImageFactory factory = (IShellItemImageFactory)item;
                IntPtr hbitmap;
                int hr = factory.GetImage(new NativeMethods.SIZE(size, size), flags, out hbitmap);
                // Sin miniatura (carpetas, tipos sin vista previa): el icono normal
                if (hr != 0 && flags != IconOnly) hr = factory.GetImage(new NativeMethods.SIZE(size, size), IconOnly, out hbitmap);
                if (hr != 0) return null;
                try
                {
                    return CopyWithAlpha(hbitmap);
                }
                finally
                {
                    NativeMethods.DeleteObject(hbitmap);
                }
            }
            catch (Exception ex)
            {
                Log.Error("[ShellImage] Sin imagen para {0}: {1}", parsingName, ex.Message);
                return null;
            }
            finally
            {
                if (item != null) Marshal.ReleaseComObject(item);
            }
        }

        /// <summary>
        /// Apps del menú Inicio (nombre e identificador). Tarda unas décimas: llamar fuera del hilo de la interfaz.
        /// </summary>
        public static List<App> StartMenuApps()
        {
            List<App> apps = new List<App>();
            IShellItem folder = null;
            IEnumShellItems items = null;
            try
            {
                SHCreateItemFromParsingName("shell:AppsFolder", IntPtr.Zero, typeof(IShellItem).GUID, out folder);
                Guid handler = BHID_EnumItems, riid = typeof(IEnumShellItems).GUID;
                IntPtr pointer;
                if (folder.BindToHandler(IntPtr.Zero, ref handler, ref riid, out pointer) != 0) return apps;
                items = (IEnumShellItems)Marshal.GetObjectForIUnknown(pointer);
                Marshal.Release(pointer);

                IShellItem item;
                uint fetched;
                while (items.Next(1, out item, out fetched) == 0 && fetched == 1)
                {
                    string name = DisplayName(item, SIGDN_NORMALDISPLAY), id = DisplayName(item, SIGDN_PARENTRELATIVEPARSING);
                    if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(id)) apps.Add(new App { Name = name, Id = id });
                    Marshal.ReleaseComObject(item);
                }
            }
            catch (Exception ex)
            {
                Log.Error("[ShellImage] No se pudieron leer las apps del menú Inicio: {0}", ex.Message);
            }
            finally
            {
                if (items != null) Marshal.ReleaseComObject(items);
                if (folder != null) Marshal.ReleaseComObject(folder);
            }
            return apps;
        }

        private static string DisplayName(IShellItem item, uint sigdn)
        {
            IntPtr name;
            if (item.GetDisplayName(sigdn, out name) != 0 || name == IntPtr.Zero) return null;
            try
            {
                return Marshal.PtrToStringUni(name);
            }
            finally
            {
                Marshal.FreeCoTaskMem(name);
            }
        }

        // Image.FromHbitmap descarta el canal alfa: se copian los bits tal cual a un mapa ARGB.
        // Las miniaturas de fotos vienen con alfa 0 en todos los píxeles: en ese caso se hacen opacas.
        private static Bitmap CopyWithAlpha(IntPtr hbitmap)
        {
            using (Bitmap raw = Image.FromHbitmap(hbitmap))
            {
                BitmapData data = raw.LockBits(new Rectangle(0, 0, raw.Width, raw.Height), ImageLockMode.ReadOnly, raw.PixelFormat);
                try
                {
                    int[] pixels = new int[raw.Width * raw.Height];
                    bool anyAlpha = false;
                    for (int y = 0; y < raw.Height; y++)
                    {
                        Marshal.Copy(new IntPtr(data.Scan0.ToInt64() + (long)y * data.Stride), pixels, y * raw.Width, raw.Width);
                    }
                    for (int i = 0; i < pixels.Length && !anyAlpha; i++) anyAlpha = (pixels[i] >> 24) != 0;
                    if (!anyAlpha)
                    {
                        for (int i = 0; i < pixels.Length; i++) pixels[i] |= unchecked((int)0xFF000000);
                    }

                    Bitmap copy = new Bitmap(raw.Width, raw.Height, PixelFormat.Format32bppArgb);
                    BitmapData target = copy.LockBits(new Rectangle(0, 0, copy.Width, copy.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                    for (int y = 0; y < copy.Height; y++)
                    {
                        Marshal.Copy(pixels, y * copy.Width, new IntPtr(target.Scan0.ToInt64() + (long)y * target.Stride), copy.Width);
                    }
                    copy.UnlockBits(target);
                    return copy;
                }
                finally
                {
                    raw.UnlockBits(data);
                }
            }
        }
    }
}
