using Dota2Optimizer.Core.Infrastructure;
namespace Dota2Optimizer.Core.Network;

public sealed record NetworkSeries(TargetResult Target, IReadOnlyList<long?> Samples);
public sealed record DoctorNetworkResult(string Adapter, string Dns, string Conclusion, IReadOnlyList<NetworkSeries> Series);

public static class DoctorNetwork
{
    public static async Task<DoctorNetworkResult> MeasureAsync(ICommandRunner commands, bool valve, CancellationToken token)
    {
        var adapter = NetworkDiagnostics.ActiveAdapter() ?? throw new InvalidOperationException("Нет активного IPv4-адаптера со шлюзом.");
        var targets = new List<(string Name,string Host,TargetKind Kind)>();
        if (adapter.Gateway != null) targets.Add(("Роутер",adapter.Gateway.ToString(),TargetKind.Gateway));
        targets.Add(("Интернет · Cloudflare","1.1.1.1",TargetKind.Internet));
        string relay = "";
        if (valve)
        {
            try
            {
                var pops = await NetworkDiagnostics.FetchValvePopsAsync(token);
                if (pops.FirstOrDefault(p => p.Ipv4 != null) is {} pop) targets.Add(("Релей Valve · "+pop.Code,pop.Ipv4!,TargetKind.Valve));
                else relay = "Список релеев пуст.";
            }
            catch (Exception e) when (!token.IsCancellationRequested) { relay = "Список релеев недоступен: "+e.Message; }
        }
        var series = await Task.WhenAll(targets.Select(async target =>
        {
            var samples = new List<long?>();
            var stats = await NetworkDiagnostics.PingAsync(target.Host,16,token,sample:samples.Add);
            return new NetworkSeries(new(target.Name,target.Host,target.Kind,stats),samples);
        }));
        string dns = "Адрес DNS недоступен";
        if (adapter.Dns.FirstOrDefault() is {} server)
        {
            var test = await DnsBenchmark.MeasureAsync("DNS адаптера",server.ToString(),token);
            dns = test.Works ? $"DNS отвечает за {test.MedianMs:0.#} мс" : "Тестовый DNS-запрос остался без ответа";
        }
        string link = adapter.Type.ToString();
        if (adapter.IsWifi)
        {
            var wifi = new NetworkDiagnostics(commands).WifiInfo();
            link = $"Wi-Fi · {wifi.Radio ?? "тип недоступен"} · сигнал {wifi.Signal?.ToString() ?? "н/д"}%";
        }
        token.ThrowIfCancellationRequested();
        return new($"{adapter.Description} · {link} · {adapter.SpeedMbps} Мбит/с",dns,Classify(series,adapter.IsWifi)+" "+relay,series);
    }

    public static string Classify(IReadOnlyList<NetworkSeries> series,bool wifi)
    {
        var all = series.Select(s=>s.Target).ToList();
        if (!all.Any(t=>t.Kind==TargetKind.Gateway && t.Stats.Received>=2) ||
            !all.Any(t=>t.Kind==TargetKind.Internet && t.Stats.Received>=2))
            return "Недостаточно ответов для локализации. Роутер или узел может фильтровать ICMP.";
        return NetworkAnalysis.Evaluate(all,wifi).Kind switch {
            VerdictKind.Local => wifi ? "Возможна нестабильность Wi-Fi. Повторите по кабелю." : "Есть отклонения до шлюза. Проверьте локальную сеть.",
            VerdictKind.Provider => "Возможна проблема маршрута за шлюзом. Повторите для нескольких направлений и сверьте потери внутри игры.",
            VerdictKind.ServerOrRoute => "Отклонение на пути к релею. Сравните игровые регионы.",
            VerdictKind.Inconclusive => "Недостаточно данных о релеях. Проверьте задержку в клиенте игры.",
            _ => "На проверенных направлениях существенных отклонений не обнаружено."
        };
    }
}
