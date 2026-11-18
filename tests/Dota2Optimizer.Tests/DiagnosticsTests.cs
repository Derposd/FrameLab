using Dota2Optimizer.Core.Benchmark;
using Dota2Optimizer.Core.Diagnostics;
using Dota2Optimizer.Core.Network;
using Dota2Optimizer.Core.SystemInfo;
using Xunit;

namespace Dota2Optimizer.Tests;

public class DiagnosticsTests
{
    private static DiagnosticSample Sample => new(new(99,null,null,0,0,null),
        new(null,null,null,null,null,null),null,[],"Нет записи",0);

    [Fact]
    public void MissingSensorsAndTotalCpuDoNotProveBottleneck()
    {
        var evidence = new DoctorEvidence(new(),Sample,null,null,0);
        Assert.Equal("Недостаточно данных",Assert.Single(DoctorRules.Evaluate(evidence,Symptom.Fps,Symptom.Heat)).Confidence);
    }

    [Fact]
    public void CpuHypothesisRequiresFrameAndCoreEvidence()
    {
        var frames=Enumerable.Repeat(20d,100).ToArray();
        var sample=Sample with { Load=Sample.Load with {GpuPercent=50}, Frames=FrameStats.FromFrameTimes(frames), Sensors=Sample.Sensors with {BusyThread=95} };
        Assert.Contains(DoctorRules.Evaluate(new(new(),sample,null,null,0),Symptom.Fps),f=>f.Title.Contains("потока"));
    }

    [Fact]
    public void FrameWindowRejectsUnusableSamplesAndRetainsSlowFrame()
    {
        double[] values=[..Enumerable.Repeat(10d,99),100,double.NaN,0,-1,double.PositiveInfinity];
        var stats=FrameStats.FromFrameTimes(values)!;
        Assert.Equal(100,stats.Frames);
        Assert.Equal(100000d/1090,stats.AvgFps,4);
        Assert.Equal(10,stats.OnePercentLowFps);
        Assert.Equal(1,stats.Stutters);
    }

    [Fact]
    public void FailedRouteIsInconclusiveAndSingleReplyHasNoJitter()
    {
        long?[] rtts=[null,8,null,null];
        var stats=PingStats.From(rtts);
        Assert.Null(stats.JitterMs);
        Assert.Equal(75,stats.LossPercent);
        Assert.StartsWith("Недостаточно",DoctorNetwork.Classify([new(new("router","router",TargetKind.Gateway,stats),rtts)],true));
        Assert.Equal(6,PingStats.From([8,12,4]).JitterMs);
    }

    [Fact]
    public void MissingPresentMonReportsCauseWithoutStartingProcess()
    {
        var capture=FpsCapture.Start(Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".exe"));
        Assert.True(capture.HasExited);
        Assert.Contains("недоступен",capture.Problem(0));
    }

    private sealed class Probe(bool fail=false) : IDiagnosticSource
    {
        public int Reads,Disposed;
        public DiagnosticSample Read(int window)
        {
            Reads++;
            return fail?throw new IOException("source unavailable"):Sample;
        }
        public void Dispose()=>Disposed++;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MonitoringReleasesSourceOnFailureOrCancellation(bool fail)
    {
        var source=new Probe(fail);
        var feed=new DiagnosticFeed(()=>source);
        using var cts=new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var run=feed.RunAsync(500,30,_=>cts.Cancel(),cts.Token);
        if(fail)await Assert.ThrowsAsync<IOException>(()=>run);
        else await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>run);
        Assert.Equal(1,source.Reads);
        Assert.Equal(1,source.Disposed);
    }

    [Fact]
    public void OverlayPreferencesSurviveRestartAndClampInvalidValues()
    {
        var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".json");
        try
        {
            var settings=new OverlayOptions(Corner:2,Mode:2,Opacity:101,Key:9,Hidden:"RAM|CPU");
            settings.Save(path);
            Assert.Equal(settings with {Opacity=100},OverlayOptions.Load(path));
        }
        finally { File.Delete(path); }
    }
}
