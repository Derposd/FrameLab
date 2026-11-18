using Dota2Optimizer.Core.SystemInfo;

namespace Dota2Optimizer.Core.Diagnostics;

public enum Symptom { Fps, Stutter, Network, Crash, Load, Heat, Input }
public sealed record Finding(string Title, string Evidence, string Confidence, string Next);
public sealed record DoctorEvidence(SystemSnapshot Pc, DiagnosticSample Sample, bool? Vsync, int? Events, int BackgroundCount);

public static class DoctorRules
{
    private sealed record Rule(Symptom[] Applies, Func<DoctorEvidence,bool> Match, Func<DoctorEvidence,Finding> Explain);
    private static readonly Rule[] Rules =
    [
        new([Symptom.Fps, Symptom.Stutter, Symptom.Input], e => e.Sample.Frames?.Stutters > 0,
            e => new("Нестабильное время кадра", $"{e.Sample.Frames!.Stutters} длинных кадров из {e.Sample.Frames.Frames}.", "Высокая для записи", "Повторите тот же участок игры и сравните 1% low.")),
        new([Symptom.Fps, Symptom.Stutter, Symptom.Input], e => e.Sample.Load.GpuPercent >= 95 && e.Sample.Frames != null,
            e => new("Возможен предел GPU", $"Системная загрузка 3D {e.Sample.Load.GpuPercent:0}% во время записи.", "Предварительная", "Повторите сцену с меньшим разрешением и сравните FPS.")),
        new([Symptom.Fps, Symptom.Stutter, Symptom.Input], e => e.Sample.Sensors.BusyThread >= 90 && e.Sample.Load.GpuPercent < 80 && e.Sample.Frames != null,
            e => new("Возможен предел одного потока", $"Занятость логического процессора {e.Sample.Sensors.BusyThread:0}%, GPU {e.Sample.Load.GpuPercent:0}%, кадр {e.Sample.Frames!.MedianFrameMs:0.0} мс.", "Предварительная", "Проверьте загрузку потоков именно игры и повторите замер без фоновой нагрузки.")),
        new([Symptom.Fps, Symptom.Stutter, Symptom.Load], e => e.Sample.Load.RamPercent >= 90,
            e => new("Мало свободной ОЗУ", $"Занято {e.Sample.Load.RamPercent:0}% физической памяти.", "Средняя", "Сравните повторный прогон после закрытия тяжёлых программ. При повторении проверьте совместимость расширения ОЗУ.")),
        new([Symptom.Stutter, Symptom.Load], e => e.Sample.Sensors.DiskBusy >= 90,
            e => new("Накопитель занят", $"Активное время дисков {e.Sample.Sensors.DiskBusy:0}%.", "Предварительная", "В мониторе ресурсов проверьте очередь диска и процессы чтения в момент фриза.")),
        new([Symptom.Fps,Symptom.Stutter], e => e.Pc.RealGpus.Count()==1 && e.Pc.PrimaryGpu is {IsIntegrated:false,VramGb:>0} gpu &&
            e.Sample.Sensors.VramGb >= gpu.VramGb * 0.9 && e.Sample.Frames?.Stutters > 0,
            e => new("Возможен дефицит видеопамяти", $"Занято {Value(e.Sample.Sensors.VramGb)} из {Value(e.Pc.PrimaryGpu!.VramGb)} ГБ при скачках времени кадра.",
                "Предварительная", "Снизьте качество текстур и сравните время кадра в той же сцене.")),
        new([Symptom.Load], e => e.Pc.Disks.Any(d=>d.Kind==DiskKind.Hdd),
            e => new("На ПК есть HDD", "Сканер Windows обнаружил механический диск.", "Требует проверки пути игры", "Проверьте размещение игры. Сравните время загрузки с доступным SSD.")),
        new([Symptom.Input], e => e.Pc.Display?.BelowMaxRefresh == true,
            e => new("Частота дисплея ниже доступной", $"{e.Pc.Display!.RefreshHz} из {e.Pc.Display.MaxRefreshHz} Гц.", "Высокая", "Откройте расширенные параметры дисплея Windows.")),
        new([Symptom.Input], e => e.Vsync == true,
            e => new("В конфигурации игры включён V-Sync", "Обнаружен setting.mat_vsync.", "Высокая для файла", "Сравните отзывчивость при обоих значениях в настройках видео игры.")),
        new([Symptom.Heat,Symptom.Stutter], e => e.Sample.Sensors.CpuTemp >= 90 || e.Sample.Sensors.GpuTemp >= 90,
            e => new("Высокая температура датчика", $"CPU {Value(e.Sample.Sensors.CpuTemp)} °C, GPU {Value(e.Sample.Sensors.GpuTemp)} °C.", "Предварительная", "Сверьте предел именно этой модели и запишите падение частоты под постоянной нагрузкой.")),
        new([Symptom.Crash], e => e.Events > 0,
            e => new("Есть события с именем игры", $"За 7 дней найдено событий приложения: {e.Events}.", "Средняя", "Откройте раздел вылетов и сопоставьте время и модуль сбоя.")),
        new([Symptom.Input,Symptom.Stutter], e => e.BackgroundCount > 0,
            e => new("Открыты фоновые приложения", $"Известных приложений: {e.BackgroundCount}.", "Низкая", "Проверьте их нагрузку во время сбоя и повторите тот же участок."))
    ];
    public static string Value(double? value) => value?.ToString("0.#") ?? "н/д";
    public static IEnumerable<Finding> Evaluate(DoctorEvidence evidence, params Symptom[] symptoms)
    {
        var found = Rules.Where(r => r.Applies.Intersect(symptoms).Any() && r.Match(evidence)).Select(r=>r.Explain(evidence)).ToList();
        if (found.Count == 0) found.Add(new("Причина пока не установлена", "Измерения не подтвердили гипотезу.", "Недостаточно данных", "Повторите проверку под нагрузкой во время проблемы."));
        return found;
    }
}
