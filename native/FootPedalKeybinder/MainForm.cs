using System.Text;

namespace FootPedalKeybinder;

internal sealed class MainForm : Form
{
    private readonly Label _deviceStatus = new();
    private readonly Label _bindingStatus = new();
    private readonly Button _refreshButton = new();
    private readonly Button _readButton = new();
    private readonly Button _captureButton = new();
    private readonly Button _writeButton = new();
    private readonly TextBox _logBox = new();
    private IReadOnlyList<HidFootSwitchDevice> _devices = [];
    private HidFootSwitchDevice? _selectedDevice;
    private KeyBinding? _pendingBinding;
    private bool _capturing;

    public MainForm()
    {
        Text = "Foot Pedal Keybinder";
        Width = 760;
        Height = 520;
        MinimumSize = new Size(640, 440);
        KeyPreview = true;
        StartPosition = FormStartPosition.CenterScreen;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(16),
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var title = new Label
        {
            Text = "Foot Pedal Keybinder",
            Font = new Font(Font.FontFamily, 20, FontStyle.Bold),
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 12),
        };

        _deviceStatus.AutoSize = true;
        _deviceStatus.Margin = new Padding(0, 0, 0, 8);

        _bindingStatus.AutoSize = true;
        _bindingStatus.Text = "Binding: none captured";
        _bindingStatus.Margin = new Padding(0, 0, 0, 12);

        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = new Padding(0, 0, 0, 12),
        };

        _refreshButton.Text = "Refresh Device";
        _refreshButton.AutoSize = true;
        _refreshButton.Click += (_, _) => RefreshDevices();

        _readButton.Text = "Read Slots";
        _readButton.AutoSize = true;
        _readButton.Enabled = false;
        _readButton.Click += (_, _) => ReadSlots();

        _captureButton.Text = "Capture Key";
        _captureButton.AutoSize = true;
        _captureButton.Click += (_, _) => StartCapture();

        _writeButton.Text = "Write Binding";
        _writeButton.AutoSize = true;
        _writeButton.Enabled = false;
        _writeButton.Click += (_, _) => WriteBinding();

        buttons.Controls.AddRange([_refreshButton, _readButton, _captureButton, _writeButton]);

        _logBox.Dock = DockStyle.Fill;
        _logBox.Multiline = true;
        _logBox.ReadOnly = true;
        _logBox.ScrollBars = ScrollBars.Vertical;
        _logBox.Font = new Font(FontFamily.GenericMonospace, 10);

        root.Controls.Add(title, 0, 0);
        root.Controls.Add(_deviceStatus, 0, 1);
        root.Controls.Add(_bindingStatus, 0, 2);
        root.Controls.Add(buttons, 0, 3);
        root.Controls.Add(_logBox, 0, 4);
        Controls.Add(root);

        KeyDown += HandleKeyDown;
        Shown += (_, _) => RefreshDevices();
    }

    private void RefreshDevices()
    {
        try
        {
            _devices = HidFootSwitchDevice.FindAll();
            _selectedDevice = _devices.FirstOrDefault();
            if (_selectedDevice is null)
            {
                _deviceStatus.Text = "Device: FS2007U1SW not found. Connect the pedal and click Refresh Device.";
                Log("No VID 3553 / PID B001 programming interface found.");
            }
            else
            {
                _deviceStatus.Text = "Device: connected to 3553:B001 programming interface.";
                Log("Connected to programming interface:");
                Log(_selectedDevice.Path);
            }
        }
        catch (Exception ex)
        {
            _selectedDevice = null;
            _deviceStatus.Text = "Device: error while scanning.";
            Log($"Scan failed: {ex.Message}");
        }

        UpdateWriteState();
    }

    private void StartCapture()
    {
        _capturing = true;
        _bindingStatus.Text = "Binding: press the key or key combo to bind.";
        Log("Capture armed.");
        Focus();
    }

    private void HandleKeyDown(object? sender, KeyEventArgs e)
    {
        if (!_capturing)
        {
            return;
        }

        e.SuppressKeyPress = true;
        if (!KeyBindings.TryFromKeyEvent(e, out var binding) || binding is null)
        {
            Log($"Unsupported key: {e.KeyCode}");
            return;
        }

        _pendingBinding = binding;
        _capturing = false;
        _bindingStatus.Text = $"Binding: {binding.Display} (modifier 0x{binding.Modifier:X2}, usage 0x{binding.Usage:X2})";
        Log($"Captured {binding.Display}.");
        UpdateWriteState();
    }

    private void WriteBinding()
    {
        if (_selectedDevice is null || _pendingBinding is null)
        {
            return;
        }

        var confirm = MessageBox.Show(
            $"Write {_pendingBinding.Display} to all internal slots on the connected FS2007U1SW?",
            "Confirm persistent write",
            MessageBoxButtons.OKCancel,
            MessageBoxIcon.Warning);

        if (confirm != DialogResult.OK)
        {
            Log("Write cancelled.");
            return;
        }

        try
        {
            var writeResults = _selectedDevice.WriteAllSlots(_pendingBinding);
            foreach (var result in writeResults)
            {
                Log(result);
            }
            foreach (var result in _selectedDevice.ReadSlots())
            {
                Log(result);
            }
            Log($"Wrote {_pendingBinding.Display} to slots 1-3. Unplug and replug the pedal, then test it.");
        }
        catch (Exception ex)
        {
            Log($"Write failed: {ex.Message}");
        }
    }

    private void UpdateWriteState()
    {
        _readButton.Enabled = _selectedDevice is not null;
        _writeButton.Enabled = _selectedDevice is not null && _pendingBinding is not null;
    }

    private void ReadSlots()
    {
        if (_selectedDevice is null)
        {
            return;
        }

        try
        {
            foreach (var result in _selectedDevice.ReadSlots())
            {
                Log(result);
            }
        }
        catch (Exception ex)
        {
            Log($"Read failed: {ex.Message}");
        }
    }

    private void Log(string message)
    {
        var builder = new StringBuilder(_logBox.Text);
        if (builder.Length > 0)
        {
            builder.AppendLine();
        }
        builder.Append(DateTime.Now.ToString("HH:mm:ss"));
        builder.Append("  ");
        builder.Append(message);
        _logBox.Text = builder.ToString();
        _logBox.SelectionStart = _logBox.Text.Length;
        _logBox.ScrollToCaret();
    }
}
