using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using StreeckSuite.Core;

namespace StreeckSuite.Modules
{
    /// <summary>
    /// Mi ciudad (mockup «Mapa de la isla»): escribes, aparecen las ciudades que coinciden y eliges una.
    /// Se cierra al elegir, con Esc o al hacer clic fuera.
    /// </summary>
    internal class CityPicker : Form
    {
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hwnd, string appName, string idList);

        private static readonly Color Back = Color.FromArgb(30, 36, 48);
        private static readonly Color Selected = Color.FromArgb(50, 56, 189, 248);
        private static readonly Color TextColor = Color.FromArgb(230, 233, 239);
        private static readonly Color Gray = Color.FromArgb(154, 164, 181);

        private readonly TextBox _search = new TextBox();
        private readonly ListBox _list = new ListBox();
        private readonly Label _statusLabel = new Label();
        private readonly System.Windows.Forms.Timer _debounce = new System.Windows.Forms.Timer { Interval = 400 };
        private readonly Action<WeatherService.Place> _choose;
        private int _query;
        private string _status = "Escribe al menos 2 letras";

        public CityPicker(float scale, Action<WeatherService.Place> choose)
        {
            _choose = choose;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = Back;
            Padding = new Padding((int)(8 * scale));
            Height = (int)(280 * scale);

            _search.Dock = DockStyle.Top;
            _search.BorderStyle = BorderStyle.FixedSingle;
            _search.BackColor = Color.FromArgb(20, 24, 33);
            _search.ForeColor = Color.White;
            _search.Font = new Font("Segoe UI", 11f);
            _search.TextChanged += (s, e) => { _debounce.Stop(); _debounce.Start(); };
            _search.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Down && _list.Items.Count > 0) { _list.Focus(); _list.SelectedIndex = 0; e.Handled = true; }
                else if (e.KeyCode == Keys.Enter && _list.Items.Count > 0) { _list.SelectedIndex = 0; Choose(); e.Handled = true; }
                else if (e.KeyCode == Keys.Escape) Close();
            };

            _list.Dock = DockStyle.Fill;
            _list.BorderStyle = BorderStyle.None;
            _list.BackColor = Back;
            _list.ForeColor = TextColor;
            _list.DrawMode = DrawMode.OwnerDrawFixed;
            _list.ItemHeight = (int)(36 * scale);
            _list.IntegralHeight = false;
            _list.DrawItem += OnDrawItem;
            _list.MouseClick += (s, e) => Choose();
            _list.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter) Choose();
                else if (e.KeyCode == Keys.Escape) Close();
            };
            Controls.Add(_list);
            _statusLabel.Dock = DockStyle.Top;
            _statusLabel.Height = (int)(26 * scale);
            _statusLabel.ForeColor = Gray;
            _statusLabel.Font = new Font("Segoe UI", 9f);
            _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
            _statusLabel.Text = _status;
            Controls.Add(_statusLabel);
            Controls.Add(_search);

            _debounce.Tick += (s, e) => { _debounce.Stop(); Search(_search.Text); };
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            int corners = NativeMethods.DWMWCP_ROUNDSMALL;
            NativeMethods.DwmSetWindowAttribute(Handle, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref corners, sizeof(int));
            SetWindowTheme(_list.Handle, "DarkMode_Explorer", null);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _search.Focus();
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            if (!IsDisposed) BeginInvoke((Action)Close);
        }

        private void Search(string text)
        {
            int query = ++_query;
            _list.Items.Clear();
            if (text.Trim().Length < 2) { _statusLabel.Text = "Escribe al menos 2 letras"; return; }
            _statusLabel.Text = "Buscando…";
            ThreadPool.QueueUserWorkItem(_ =>
            {
                List<WeatherService.Place> places;
                try { places = WeatherService.SearchPlaces(text); }
                catch (Exception ex) { Log.Error("[CityPicker] {0}", ex.Message); places = null; }
                if (IsDisposed || !IsHandleCreated) return;
                BeginInvoke((Action)(() =>
                {
                    if (query != _query || IsDisposed) return; // Ya se escribió otra cosa
                    _list.Items.Clear();
                    if (places == null) _statusLabel.Text = "Sin internet: intenta de nuevo";
                    else if (places.Count == 0) _statusLabel.Text = "No encontré esa ciudad";
                    else
                    {
                        _statusLabel.Text = places.Count + (places.Count == 1 ? " ciudad" : " ciudades") + " · elige la tuya";
                        foreach (WeatherService.Place p in places) _list.Items.Add(p);
                    }
                }));
            });
        }

        private void OnDrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            WeatherService.Place place = (WeatherService.Place)_list.Items[e.Index];
            bool selected = (e.State & DrawItemState.Selected) != 0;
            using (SolidBrush back = new SolidBrush(selected ? Selected : Back)) e.Graphics.FillRectangle(back, e.Bounds);
            Rectangle text = new Rectangle(e.Bounds.X + 8, e.Bounds.Y, e.Bounds.Width - 16, e.Bounds.Height / 2 + 2);
            Rectangle sub = new Rectangle(e.Bounds.X + 8, e.Bounds.Y + e.Bounds.Height / 2 - 2, e.Bounds.Width - 16, e.Bounds.Height / 2);
            TextRenderer.DrawText(e.Graphics, place.Name, new Font("Segoe UI Semibold", 10f), text, TextColor, TextFormatFlags.Bottom | TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(e.Graphics, (place.Region + (place.Region.Length > 0 && place.Country.Length > 0 ? ", " : "") + place.Country), new Font("Segoe UI", 8.5f), sub, Gray, TextFormatFlags.Top | TextFormatFlags.EndEllipsis);
        }

        private void Choose()
        {
            WeatherService.Place place = _list.SelectedItem as WeatherService.Place;
            if (place == null) return;
            _choose(place);
            Close();
        }
    }
}
