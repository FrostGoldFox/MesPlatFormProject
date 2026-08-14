using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SmartFactoryActuator.Shared.Config;

namespace VirtualPLCTestWPF;

public partial class MainWindow : Window
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly bool[] _commandValues = new bool[8];

    public MainWindow()
    {
        InitializeComponent();
    }

    private async void OnCommandClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag } button || !int.TryParse(tag, out int command))
        {
            return;
        }

        ushort? targetPressure = command switch { 2 => 250, 3 => 500, 4 => 750, _ => null };
        bool nextValue = targetPressure.HasValue || !_commandValues[command - 1];
        button.IsEnabled = false;
        try
        {
            CommandResponse response = await SendCommandAsync(command, nextValue, targetPressure);
            if (!response.Accepted)
            {
                ConnectionStatusText.Text = $"Command {command} 거부: {response.Message}";
                ConnectionStatusText.Foreground = Brushes.IndianRed;
                return;
            }

            _commandValues[command - 1] = response.Value;
            if (targetPressure.HasValue)
            {
                button.Content = $"Command {command}: TargetPressure={response.TargetPressure}";
                ConnectionStatusText.Text = $"Command {command} 전송 성공: TargetPressure={response.TargetPressure}";
            }
            else
            {
                button.Content = command == 1
                    ? $"Command 1: Position 1 Occupied={(response.Value ? "ON" : "OFF")}"
                    : $"Command {command}: Value={(response.Value ? "ON" : "OFF")}";
                ConnectionStatusText.Text = $"Command {command} 전송 성공: {response.Value}";
            }
            ConnectionStatusText.Foreground = Brushes.ForestGreen;
        }
        catch (Exception exception) when (exception is SocketException or IOException or OperationCanceledException or JsonException)
        {
            ConnectionStatusText.Text = $"Command {command} 전송 실패: {exception.Message}";
            ConnectionStatusText.Foreground = Brushes.IndianRed;
        }
        finally
        {
            button.IsEnabled = true;
        }
    }

    private static async Task<CommandResponse> SendCommandAsync(int command, bool value, ushort? targetPressure)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        using var client = new TcpClient();
        await client.ConnectAsync(EnvironmentConfig.Host, EnvironmentConfig.ReservedPort1, timeout.Token);

        using NetworkStream stream = client.GetStream();
        using var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
        using var reader = new StreamReader(stream, Encoding.UTF8);
        await writer.WriteLineAsync(JsonSerializer.Serialize(new { Command = command, Value = value, TargetPressure = targetPressure }, JsonOptions));

        string? response = await reader.ReadLineAsync(timeout.Token);
        return response is null
            ? throw new IOException("TestNetWork가 응답 없이 연결을 종료했습니다.")
            : JsonSerializer.Deserialize<CommandResponse>(response, JsonOptions)
              ?? throw new JsonException("TestNetWork 응답이 비어 있습니다.");
    }

    private sealed record CommandResponse(bool Accepted, int Command, bool Value, ushort? TargetPressure, string? Message);
}