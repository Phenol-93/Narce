using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace Nacre.Services;

public sealed class SensitiveClipboard : IDisposable
{
    [DllImport("user32.dll")] private static extern uint GetClipboardSequenceNumber();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private byte[]? _fingerprint;
    private uint _sequence;
    private DateTimeOffset _expires;
    private int _attempts;
    public SensitiveClipboard() => _timer.Tick += Tick;
    public void Copy(string text, bool sensitive, int seconds)
    {
        var data = new DataObject();
        data.SetText(text, TextDataFormat.UnicodeText);
        // Windows clipboard history and cloud clipboard opt-out formats.
        if (sensitive)
        {
            data.SetData("CanIncludeInClipboardHistory", new MemoryStream(BitConverter.GetBytes(0)));
            data.SetData("CanUploadToCloudClipboard", new MemoryStream(BitConverter.GetBytes(0)));
            data.SetData("ExcludeClipboardContentFromMonitorProcessing", new MemoryStream(BitConverter.GetBytes(1)));
        }
        Clipboard.SetDataObject(data, copy: true);
        Forget();
        if (!sensitive) return;
        _fingerprint = Hash(text);
        _sequence = GetClipboardSequenceNumber();
        _expires = DateTimeOffset.UtcNow.AddSeconds(seconds);
        _attempts = 0;
        if (seconds > 0) _timer.Start();
    }
    private static byte[] Hash(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        try { return SHA256.HashData(bytes); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
    private void Tick(object? sender, EventArgs e) { if (DateTimeOffset.UtcNow >= _expires) TryClearOwned(); }
    public void TryClearOwned()
    {
        if (_fingerprint is null) return;
        try
        {
            if (GetClipboardSequenceNumber() != _sequence) { Forget(); return; }
            if (Clipboard.ContainsText())
            {
                var hash = Hash(Clipboard.GetText());
                try { if (CryptographicOperations.FixedTimeEquals(hash, _fingerprint)) Clipboard.Clear(); }
                finally { CryptographicOperations.ZeroMemory(hash); }
            }
            Forget();
        }
        catch (ExternalException)
        {
            if (++_attempts > 5) Forget();
            else { _expires = DateTimeOffset.UtcNow.AddSeconds(1); _timer.Start(); }
        }
    }
    private void Forget()
    { _timer.Stop(); if (_fingerprint is not null) CryptographicOperations.ZeroMemory(_fingerprint); _fingerprint = null; }
    public void Dispose() { TryClearOwned(); Forget(); _timer.Tick -= Tick; }
}
