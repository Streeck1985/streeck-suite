using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using StreeckSuite.Core;

namespace StreeckSuite.Modules
{
    /// <summary>
    /// Enviar y recibir con el celular (mockup aprobado y prototipo probado): una página pequeñita servida por la
    /// propia PC en la red de casa. Nada pasa por internet. La dirección lleva una clave al azar, así que solo entra
    /// quien escanea el QR. Se apaga sola 10 minutos después de cerrar la ventana.
    /// </summary>
    internal static class PhoneLink
    {
        private const int BasePort = 8765;
        public static readonly string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "Recibido del celular");

        private static TcpListener _listener;
        private static string _ip, _token;
        private static int _port;

        public static volatile string OfferedFile;   // Archivo que la PC ofrece al celular
        public static volatile string OfferedText;   // Texto largo que no cabe en un QR
        public static DateTime LastActivity = DateTime.Now;

        public static event Action<string, long> FileReceived;   // Ruta, bytes
        public static event Action<string> TextReceived;
        public static event Action<string, float> Progress;      // Nombre, 0..1

        public static bool IsRunning { get { return _listener != null; } }

        public static string BaseUrl
        {
            get { return "http://" + _ip + ":" + _port + "/" + _token + "/"; }
        }

        public static string Address
        {
            get { return _ip + ":" + _port; }
        }

        public static bool Start()
        {
            if (_listener != null) return true;
            _ip = LocalAddress();
            if (_ip == null) { Log.Error("[PhoneLink] No hay red local"); return false; }
            byte[] random = new byte[6];
            using (System.Security.Cryptography.RandomNumberGenerator rng = System.Security.Cryptography.RandomNumberGenerator.Create()) rng.GetBytes(random);
            _token = BitConverter.ToString(random).Replace("-", "").ToLowerInvariant();
            for (int port = BasePort; port < BasePort + 10; port++)
            {
                try
                {
                    TcpListener listener = new TcpListener(IPAddress.Any, port);
                    listener.Start();
                    _listener = listener;
                    _port = port;
                    break;
                }
                catch (SocketException) { }
            }
            if (_listener == null) { Log.Error("[PhoneLink] No hay puerto libre"); return false; }
            LastActivity = DateTime.Now;
            Thread accept = new Thread(AcceptLoop) { IsBackground = true, Name = "PhoneLink" };
            accept.Start(_listener);
            Log.Info("[PhoneLink] Esperando al celular en {0}", Address);
            return true;
        }

        public static void Stop()
        {
            TcpListener listener = _listener;
            _listener = null;
            OfferedFile = null;
            OfferedText = null;
            if (listener != null) { try { listener.Stop(); } catch (Exception) { } Log.Info("[PhoneLink] Apagado"); }
        }

        // La dirección de la PC en la red de casa: la de la tarjeta con salida (puerta de enlace) y dirección privada
        private static string LocalAddress()
        {
            string fallback = null;
            foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback || nic.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;
                IPInterfaceProperties props = nic.GetIPProperties();
                bool hasGateway = false;
                foreach (GatewayIPAddressInformation gw in props.GatewayAddresses)
                    if (gw.Address.AddressFamily == AddressFamily.InterNetwork && !gw.Address.Equals(IPAddress.Any)) hasGateway = true;
                foreach (UnicastIPAddressInformation addr in props.UnicastAddresses)
                {
                    if (addr.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    byte[] b = addr.Address.GetAddressBytes();
                    bool isPrivate = b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168);
                    if (!isPrivate) continue;
                    if (hasGateway) return addr.Address.ToString();
                    if (fallback == null) fallback = addr.Address.ToString();
                }
            }
            return fallback;
        }

        private static void AcceptLoop(object state)
        {
            TcpListener listener = (TcpListener)state;
            while (_listener == listener)
            {
                try
                {
                    TcpClient client = listener.AcceptTcpClient();
                    ThreadPool.QueueUserWorkItem(_ =>
                    {
                        try { Handle(client); }
                        catch (Exception ex) { Log.Error("[PhoneLink] {0}", ex.Message); }
                        finally { client.Close(); }
                    });
                }
                catch (Exception)
                {
                    return; // Se apagó
                }
            }
        }

        private static void Handle(TcpClient client)
        {
            client.ReceiveTimeout = 30000;
            NetworkStream stream = client.GetStream();
            List<byte> head = new List<byte>();
            while (true)
            {
                int b = stream.ReadByte();
                if (b < 0) return;
                head.Add((byte)b);
                int n = head.Count;
                if (n >= 4 && head[n - 4] == 13 && head[n - 3] == 10 && head[n - 2] == 13 && head[n - 1] == 10) break;
                if (n > 16384) return;
            }
            string[] lines = Encoding.UTF8.GetString(head.ToArray()).Split(new[] { "\r\n" }, StringSplitOptions.None);
            string[] first = lines[0].Split(' ');
            if (first.Length < 2) return;
            string method = first[0], path = first[1];
            long length = 0;
            foreach (string l in lines)
                if (l.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)) long.TryParse(l.Substring(15).Trim(), out length);

            // Sin la clave del QR no se atiende nada
            string prefix = "/" + _token + "/";
            if (!path.StartsWith(prefix)) { Reply(stream, "404 Not Found", "text/plain", "No"); return; }
            string route = path.Substring(prefix.Length);
            LastActivity = DateTime.Now;

            if (method == "GET" && route.StartsWith("descargar"))
            {
                SendFile(stream, OfferedFile);
            }
            else if (method == "GET" && route.StartsWith("texto-pc"))
            {
                Reply(stream, "200 OK", "text/html; charset=utf-8", TextPage(OfferedText));
            }
            else if (method == "GET")
            {
                Reply(stream, "200 OK", "text/html; charset=utf-8", PhonePage());
            }
            else if (method == "POST" && route.StartsWith("archivo"))
            {
                ReceiveFile(stream, route, length);
            }
            else if (method == "POST" && route.StartsWith("texto"))
            {
                if (length > 1000000) { Reply(stream, "413 Payload Too Large", "text/plain", "No"); return; }
                MemoryStream m = new MemoryStream();
                Copy(stream, m, length, null);
                Reply(stream, "200 OK", "text/plain", "ok");
                string text = Encoding.UTF8.GetString(m.ToArray());
                Action<string> handler = TextReceived;
                if (handler != null && text.Length > 0) handler(text);
            }
            else
            {
                Reply(stream, "404 Not Found", "text/plain", "No");
            }
        }

        private static void ReceiveFile(NetworkStream stream, string route, long length)
        {
            string name = "archivo";
            int q = route.IndexOf("nombre=", StringComparison.Ordinal);
            if (q >= 0) name = Uri.UnescapeDataString(route.Substring(q + 7));
            name = Path.GetFileName(name);
            foreach (char bad in Path.GetInvalidFileNameChars()) name = name.Replace(bad, '_');
            if (name.Length == 0) name = "archivo";
            Directory.CreateDirectory(Folder);
            string target = Path.Combine(Folder, name);
            for (int i = 1; File.Exists(target); i++)
                target = Path.Combine(Folder, Path.GetFileNameWithoutExtension(name) + " (" + i + ")" + Path.GetExtension(name));

            string shown = Path.GetFileName(target);
            using (FileStream file = File.Create(target)) Copy(stream, file, length, f => { Action<string, float> p = Progress; if (p != null) p(shown, f); });
            Reply(stream, "200 OK", "text/plain", "ok");
            Action<string, long> done = FileReceived;
            if (done != null) done(target, length);
        }

        private static void SendFile(NetworkStream stream, string path)
        {
            if (path == null || !File.Exists(path)) { Reply(stream, "404 Not Found", "text/plain", "Ya no está disponible"); return; }
            FileInfo info = new FileInfo(path);
            string name = info.Name;
            string ascii = Encoding.ASCII.GetString(Encoding.ASCII.GetBytes(name)).Replace('?', '_').Replace("\"", "");
            byte[] head = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: application/octet-stream\r\nContent-Length: " + info.Length +
                "\r\nContent-Disposition: attachment; filename=\"" + ascii + "\"; filename*=UTF-8''" + Uri.EscapeDataString(name) + "\r\nConnection: close\r\n\r\n");
            stream.Write(head, 0, head.Length);
            using (FileStream file = File.OpenRead(path)) file.CopyTo(stream, 81920);
            stream.Flush();
        }

        private static void Copy(Stream from, Stream to, long length, Action<float> progress)
        {
            byte[] buffer = new byte[81920];
            long left = length, done = 0;
            int lastStep = -1;
            while (left > 0)
            {
                int r = from.Read(buffer, 0, (int)Math.Min(buffer.Length, left));
                if (r <= 0) break;
                to.Write(buffer, 0, r);
                left -= r;
                done += r;
                int step = (int)(done * 20 / Math.Max(1, length)); // Cada 5 %
                if (progress != null && step != lastStep) { lastStep = step; progress(done / (float)length); }
            }
        }

        private static void Reply(NetworkStream s, string status, string type, string body)
        {
            byte[] data = Encoding.UTF8.GetBytes(body);
            byte[] head = Encoding.ASCII.GetBytes("HTTP/1.1 " + status + "\r\nContent-Type: " + type + "\r\nContent-Length: " + data.Length +
                "\r\nConnection: close\r\nCache-Control: no-store\r\n\r\n");
            s.Write(head, 0, head.Length);
            s.Write(data, 0, data.Length);
            s.Flush();
        }

        private static string Html(string text)
        {
            return WebUtility.HtmlEncode(text ?? "");
        }

        private const string Style =
            "<style>*{box-sizing:border-box}body{margin:0 auto;max-width:560px;background:#000;color:#fff;font-family:system-ui,sans-serif;padding:28px 20px}" +
            ".e{display:flex;gap:7px;margin-bottom:16px}.e i{width:12px;height:22px;background:#fff;border-radius:6px;display:block}" +
            "h1{font-size:26px;margin:0 0 4px}h2{font-size:17px;margin:28px 0 8px}p{color:#9aa1b2;margin:0 0 18px}" +
            ".big{display:block;width:100%;background:#fff;color:#000;border:0;border-radius:18px;padding:18px;font-size:18px;font-weight:700;text-align:center;text-decoration:none}" +
            "textarea{width:100%;background:#16181d;color:#fff;border:1px solid #2a2d35;border-radius:14px;padding:12px;font-size:16px;min-height:90px;margin-top:22px}" +
            ".sec{display:block;width:100%;background:#16181d;color:#fff;border:1px solid #2a2d35;border-radius:14px;padding:14px;font-size:16px;margin-top:8px}" +
            ".item{background:#16181d;border-radius:12px;padding:10px 12px;margin-top:8px;font-size:14px}.bar{height:5px;background:#2a2d35;border-radius:3px;margin-top:6px;overflow:hidden}" +
            ".bar i{display:block;height:100%;width:0;background:#4ad991}.note{font-size:12px;color:#6b7280;margin-top:28px}pre{white-space:pre-wrap;background:#16181d;border-radius:14px;padding:14px}</style>";

        private static string PhonePage()
        {
            string offered = "";
            string file = OfferedFile;
            if (file != null && File.Exists(file))
                offered = "<h2>Desde tu PC</h2><a class='big' href='descargar'>Descargar " + Html(Path.GetFileName(file)) + "</a>";
            else if (OfferedText != null)
                offered = "<h2>Desde tu PC</h2><a class='big' href='texto-pc'>Ver el texto</a>";
            return "<!doctype html><html lang='es'><head><meta charset='utf-8'><meta name='viewport' content='width=device-width,initial-scale=1'><title>Enviar a mi PC</title>" + Style + "</head>" +
                "<body><div class='e'><i></i><i></i></div><h1>Enviar a mi PC</h1><p>Fotos, videos, PDF o lo que quieras.</p>" + offered +
                "<label class='big' style='margin-top:14px'>Elegir archivos<input id='f' type='file' multiple hidden></label><div id='list'></div>" +
                "<textarea id='t' placeholder='O pega aquí un texto o link…'></textarea><button class='sec' id='s'>Copiar en la PC</button>" +
                "<p class='note'>Conexión directa con tu PC por tu Wi-Fi; nada sale a internet. Puedes dejar esta página abierta para mandar más.</p>" +
                "<script>" +
                "document.getElementById('f').onchange=async function(){for(const file of this.files){const d=document.createElement('div');d.className='item';d.innerHTML='<span></span><div class=bar><i></i></div>';d.firstChild.textContent=file.name;document.getElementById('list').appendChild(d);" +
                "await new Promise(r=>{const x=new XMLHttpRequest();x.open('POST','archivo?nombre='+encodeURIComponent(file.name));x.upload.onprogress=e=>{d.querySelector('i').style.width=(e.loaded/e.total*100)+'%'};x.onload=()=>{d.firstChild.textContent='✓ '+file.name;d.querySelector('i').style.width='100%';r()};x.onerror=()=>{d.firstChild.textContent='✗ No se pudo enviar '+file.name;r()};x.send(file);});}this.value='';};" +
                "document.getElementById('s').onclick=async function(){const t=document.getElementById('t');if(!t.value)return;try{await fetch('texto',{method:'POST',body:t.value});this.textContent='✓ Copiado en la PC';t.value='';}catch(e){this.textContent='✗ No se pudo enviar';}setTimeout(()=>this.textContent='Copiar en la PC',2000);};" +
                "</script></body></html>";
        }

        private static string TextPage(string text)
        {
            return "<!doctype html><html lang='es'><head><meta charset='utf-8'><meta name='viewport' content='width=device-width,initial-scale=1'><title>Texto de tu PC</title>" + Style + "</head>" +
                "<body><div class='e'><i></i><i></i></div><h1>Texto de tu PC</h1><pre id='x'>" + Html(text) + "</pre>" +
                "<button class='big' onclick=\"navigator.clipboard.writeText(document.getElementById('x').textContent).then(()=>this.textContent='✓ Copiado')\">Copiar</button></body></html>";
        }
    }

    /// <summary>
    /// Ventana del celular: baja desde la isla con el QR. Recibir (Ctrl+Alt+R) o enviar lo que copiaste / un archivo.
    /// </summary>
    internal class PhoneWindow : Form
    {
        private const float LogicalW = 460, LogicalH = 300, Pad = 14, Radius = 26;
        private enum Mode { Receive, Send }

        private readonly float _scale;
        private Mode _mode = Mode.Receive;
        private Bitmap _qr;
        private string _title = "", _detail = "", _note = "";
        private string _lastReceived;
        private float _progress = -1;
        private string _progressName;
        private DateTime _shownAt;
        private readonly System.Windows.Forms.Timer _idle = new System.Windows.Forms.Timer { Interval = 30000 };
        private readonly List<KeyValuePair<RectangleF, Action>> _hits = new List<KeyValuePair<RectangleF, Action>>();
        private readonly Font _titleFont = new Font("Segoe UI Semibold", 12f);
        private readonly Font _textFont = new Font("Segoe UI", 9f);
        private readonly Font _buttonFont = new Font("Segoe UI Semibold", 9f);

        public PhoneWindow()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.Black;
            DoubleBuffered = true;
            Text = "Streeck Suite · Celular";
            using (Graphics g = CreateGraphics()) _scale = g.DpiX / 96f;
            // Sin la ventana abierta, el servidor se apaga a los 10 minutos sin uso
            _idle.Tick += (s, e) => { if (!Visible && PhoneLink.IsRunning && (DateTime.Now - PhoneLink.LastActivity).TotalMinutes > 10) PhoneLink.Stop(); };
            _idle.Start();
            PhoneLink.Progress += (name, f) => UiInvoke(() => { _progressName = name; _progress = f >= 1 ? -1 : f; Invalidate(); });
            PhoneLink.FileReceived += (path, size) => UiInvoke(() => { _lastReceived = Path.GetFileName(path); _progress = -1; Invalidate(); });
        }

        private void UiInvoke(Action action)
        {
            if (IsHandleCreated && !IsDisposed) BeginInvoke(action);
        }

        private float Px(float v) { return v * _scale; }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x80;
                return cp;
            }
        }

        public void ShowReceive()
        {
            if (Visible && _mode == Mode.Receive) { Hide(); return; }
            if (!PhoneLink.Start()) { EventBus.Instance.Publish(StreeckSuite.Core.Events.NotchShowMessage, "⚠️ No hay red para conectar el celular"); return; }
            _mode = Mode.Receive;
            SetQr(PhoneLink.BaseUrl);
            _title = "Recibir del celular";
            _detail = "Escanea con la cámara del celular (mismo Wi-Fi).";
            _note = "Se guarda en Descargas › Recibido del celular";
            Open();
        }

        public void SendText(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            _mode = Mode.Send;
            if (Encoding.UTF8.GetByteCount(text) <= 600)
            {
                // Cabe en el QR: la cámara lo muestra sin conectarse a nada
                SetQr(text);
                bool link = text.StartsWith("http://") || text.StartsWith("https://");
                _title = link ? "Abrir el link en el celular" : "Pasar el texto al celular";
                _detail = "Escanea con la cámara; no hace falta la misma red.";
            }
            else
            {
                if (!PhoneLink.Start()) return;
                PhoneLink.OfferedText = text;
                PhoneLink.OfferedFile = null;
                SetQr(PhoneLink.BaseUrl + "texto-pc");
                _title = "Pasar el texto al celular";
                _detail = "Escanea con la cámara (mismo Wi-Fi).";
            }
            _note = Shorten(text.Replace("\r", " ").Replace("\n", " "), 60);
            Open();
        }

        public void SendFile(string path)
        {
            if (path == null || !File.Exists(path)) return;
            if (!PhoneLink.Start()) { EventBus.Instance.Publish(StreeckSuite.Core.Events.NotchShowMessage, "⚠️ No hay red para conectar el celular"); return; }
            _mode = Mode.Send;
            PhoneLink.OfferedFile = path;
            PhoneLink.OfferedText = null;
            SetQr(PhoneLink.BaseUrl + "descargar");
            _title = "Pasar el archivo al celular";
            _detail = "Escanea con la cámara (mismo Wi-Fi).";
            _note = Shorten(Path.GetFileName(path), 40) + " · " + SizeText(new FileInfo(path).Length);
            Open();
        }

        /// <summary>
        /// Lo último que copiaste: texto o link por QR; archivo o imagen, por la red.
        /// </summary>
        public void SendItem(ClipItem item)
        {
            if (item == null) return;
            if (item.Kind == ClipItem.ItemKind.Files)
            {
                string[] files = item.Text.Split('\n');
                if (files.Length == 1 && File.Exists(files[0])) SendFile(files[0]);
                else EventBus.Instance.Publish(StreeckSuite.Core.Events.NotchShowMessage, "📱 Elige un solo archivo para pasarlo");
            }
            else if (item.Kind == ClipItem.ItemKind.Image && item.Image != null)
            {
                string temp = Path.Combine(Path.GetTempPath(), "Imagen " + DateTime.Now.ToString("yyyy-MM-dd HHmmss") + ".png");
                item.Image.Save(temp, System.Drawing.Imaging.ImageFormat.Png);
                SendFile(temp);
            }
            else SendText(item.Text);
        }

        private void SetQr(string text)
        {
            QrCode code = QrCode.Encode(text);
            if (_qr != null) _qr.Dispose();
            _qr = code != null ? code.ToBitmap(Math.Max(3, (int)(Px(200) / (code.Size + 8)))) : null;
        }

        private void Open()
        {
            Rectangle island = IslandEyes.IslandRect;
            Screen screen = island.Width > 0 ? Screen.FromRectangle(island) : Screen.PrimaryScreen;
            int w = (int)Px(LogicalW), h = (int)Px(LogicalH);
            int centerX = island.Width > 0 ? island.Left + island.Width / 2 : screen.Bounds.Left + screen.Bounds.Width / 2;
            Bounds = new Rectangle(Math.Max(screen.Bounds.Left, Math.Min(screen.Bounds.Right - w, centerX - w / 2)), screen.Bounds.Top, w, h);
            using (GraphicsPath shape = BottomRounded(new RectangleF(0, 0, w, h), Px(Radius))) Region = new Region(shape);
            Invalidate();
            Show();
            Activate();
            _shownAt = DateTime.Now;
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            if (Visible && (DateTime.Now - _shownAt).TotalMilliseconds > 400) Hide();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape) { Hide(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            foreach (KeyValuePair<RectangleF, Action> hit in _hits)
                if (hit.Key.Contains(e.Location)) { hit.Value(); return; }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Black);
            _hits.Clear();
            Color gray = Color.FromArgb(170, 176, 190), soft = Color.FromArgb(30, 32, 38);

            // QR a la izquierda
            float qrSize = Px(212);
            RectangleF qrRect = new RectangleF(Px(Pad), Px(Pad), qrSize, qrSize);
            IslandPainter.FillRounded(g, qrRect, Px(16), Color.White);
            if (_qr != null)
            {
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                float inner = qrSize - Px(12);
                g.DrawImage(_qr, qrRect.X + Px(6), qrRect.Y + Px(6), inner, inner);
                g.PixelOffsetMode = PixelOffsetMode.Default;
            }

            // Textos a la derecha
            float x = qrRect.Right + Px(16), w = Width - x - Px(Pad);
            IslandEyes.DrawPreview(g, new PointF(x + Px(18), Px(Pad + 12)), 0.5f * _scale, _lastReceived != null ? IslandEyes.Mood.Happy : IslandEyes.Mood.Idle);
            float y = Px(Pad + 32);
            TextRenderer.DrawText(g, _title, _titleFont, Rectangle.Round(new RectangleF(x, y, w, Px(24))), Color.White, TextFormatFlags.EndEllipsis);
            y += Px(26);
            TextRenderer.DrawText(g, _detail, _textFont, Rectangle.Round(new RectangleF(x, y, w, Px(36))), gray, TextFormatFlags.WordBreak);
            y += Px(38);
            TextRenderer.DrawText(g, _note, _textFont, Rectangle.Round(new RectangleF(x, y, w, Px(36))), gray, TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis);
            y += Px(38);

            if (_mode == Mode.Receive)
            {
                string status = _progress >= 0 ? string.Format("Recibiendo {0}… {1:0} %", Shorten(_progressName, 22), _progress * 100)
                              : _lastReceived != null ? "✓ Llegó " + Shorten(_lastReceived, 26) : "Si no abre: " + PhoneLink.Address;
                TextRenderer.DrawText(g, status, _textFont, Rectangle.Round(new RectangleF(x, y, w, Px(20))), _progress >= 0 || _lastReceived != null ? Color.FromArgb(74, 217, 145) : gray, TextFormatFlags.EndEllipsis);
                if (_progress >= 0)
                {
                    RectangleF track = new RectangleF(x, y + Px(22), w, Px(5));
                    IslandPainter.FillRounded(g, track, Px(2.5f), soft);
                    IslandPainter.FillRounded(g, new RectangleF(track.X, track.Y, Math.Max(Px(5), track.Width * _progress), track.Height), Px(2.5f), Color.FromArgb(74, 217, 145));
                }
            }

            // Botones abajo
            float by = Height - Px(Pad) - Px(32);
            RectangleF first = new RectangleF(x, by, (w - Px(8)) / 2, Px(32));
            RectangleF second = new RectangleF(first.Right + Px(8), by, first.Width, Px(32));
            if (_mode == Mode.Receive)
            {
                Button(g, first, "Abrir carpeta", () => { Directory.CreateDirectory(PhoneLink.Folder); Process.Start("explorer.exe", "\"" + PhoneLink.Folder + "\""); Hide(); });
                Button(g, second, "Enviar lo copiado", () =>
                {
                    List<ClipItem> items = ClipboardHistory.Items;
                    if (items.Count > 0) SendItem(items[0]);
                    else EventBus.Instance.Publish(StreeckSuite.Core.Events.NotchShowMessage, "📱 Copia algo primero para enviarlo");
                });
            }
            else
            {
                Button(g, first, "Recibir del celular", ShowReceive);
                Button(g, second, "Cerrar", Hide);
            }
        }

        private void Button(Graphics g, RectangleF r, string text, Action action)
        {
            IslandPainter.FillRounded(g, r, Px(10), Color.FromArgb(30, 32, 38));
            TextRenderer.DrawText(g, text, _buttonFont, Rectangle.Round(r), Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            _hits.Add(new KeyValuePair<RectangleF, Action>(r, action));
        }

        private static string Shorten(string text, int max)
        {
            if (text == null) return "";
            return text.Length <= max ? text : text.Substring(0, max - 1) + "…";
        }

        internal static string SizeText(long bytes)
        {
            if (bytes >= 1048576) return (bytes / 1048576.0).ToString("0.0") + " MB";
            return Math.Max(1, bytes / 1024) + " KB";
        }

        private static GraphicsPath BottomRounded(RectangleF r, float radius)
        {
            GraphicsPath p = new GraphicsPath();
            float d = radius * 2;
            p.AddLine(r.Left, r.Top, r.Right, r.Top);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }
}
