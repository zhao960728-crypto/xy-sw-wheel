using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace XY.SolidWorksPlugin.Tests;

public sealed class ConfigPersistenceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "xy-config-atomic-" + Guid.NewGuid().ToString("N"));
    private string ConfigPath => Path.Combine(_directory, "wheel.json");

    [Fact]
    public void NewAndExistingConfigRoundTripUnicodeAndShorterContent()
    {
        var config = ConfigStore.Deserialize("{}"); ConfigStore.Normalize(config);
        config.WheelCommandTabName = "中文轮盘 " + new string('A', 4096);
        ConfigStore.WriteConfigFile(ConfigPath, ConfigStore.Serialize(config));
        Assert.Equal(config.WheelCommandTabName, ConfigStore.Deserialize(File.ReadAllText(ConfigPath, Encoding.UTF8)).WheelCommandTabName);
        config.WheelCommandTabName = "轮盘"; config.Menus[SceneKeys.Sketch].Enabled = false;
        ConfigStore.WriteConfigFile(ConfigPath, ConfigStore.Serialize(config));
        var restored = ConfigStore.Deserialize(File.ReadAllText(ConfigPath, Encoding.UTF8));
        Assert.Equal("轮盘", restored.WheelCommandTabName);
        Assert.False(restored.Menus[SceneKeys.Sketch].Enabled);
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp-*"));
    }

    [Fact]
    public void FailedReplacementPreservesExistingBytesAndCanBeRetried()
    {
        const string original = "{\"WheelCommandTabName\":\"原轮盘\"}";
        ConfigStore.WriteConfigFile(ConfigPath, original);
        byte[] before = File.ReadAllBytes(ConfigPath);
        using (var reader = new FileStream(ConfigPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Assert.Throws<IOException>(() => ConfigStore.WriteConfigFile(ConfigPath, "{\"WheelCommandTabName\":\"新轮盘\"}"));
            Assert.Equal(before, File.ReadAllBytes(ConfigPath));
            Assert.Empty(Directory.GetFiles(_directory, "*.tmp-*"));
        }
        ConfigStore.WriteConfigFile(ConfigPath, "{\"WheelCommandTabName\":\"新轮盘\"}");
        Assert.Equal("新轮盘", ConfigStore.Deserialize(File.ReadAllText(ConfigPath, Encoding.UTF8)).WheelCommandTabName);
    }

    [Fact]
    public void ConcurrentReadersOnlyObserveCompleteConfigurations()
    {
        string first = "{\"WheelCommandTabName\":\"第一套\",\"FontSizePx\":12}";
        string second = "{\"WheelCommandTabName\":\"第二套\",\"FontSizePx\":20}";
        ConfigStore.WriteConfigFile(ConfigPath, first);
        var errors = new ConcurrentQueue<Exception>();
        using var started = new ManualResetEventSlim();
        using var finished = new ManualResetEventSlim();
        int reads = 0;
        var reader = new Thread(() =>
        {
            try
            {
                do
                {
                    var config = ConfigStore.ReadConfigFile(ConfigPath);
                    Assert.True((config.WheelCommandTabName == "第一套" && config.FontSizePx == 12)
                        || (config.WheelCommandTabName == "第二套" && config.FontSizePx == 20));
                    Interlocked.Increment(ref reads); started.Set();
                } while (!finished.IsSet);
            }
            catch (Exception error) { errors.Enqueue(error); started.Set(); }
        }) { IsBackground = true };
        reader.Start();
        Assert.True(started.Wait(5000));
        try { Parallel.For(0, 40, i => ConfigStore.WriteConfigFile(ConfigPath, i % 2 == 0 ? second : first)); }
        finally { finished.Set(); Assert.True(reader.Join(5000)); }
        Assert.Empty(errors); Assert.True(reads > 0);
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp-*"));
    }

    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
}
