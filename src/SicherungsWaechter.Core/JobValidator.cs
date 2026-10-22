namespace SicherungsWaechter.Core;

public sealed record ValidationIssue(bool IsError, string Message);

public static class JobValidator
{
    public static List<ValidationIssue> Validate(BackupJob job, IEnumerable<BackupJob> otherJobs)
    {
        var issues = new List<ValidationIssue>();
        if (string.IsNullOrWhiteSpace(job.Source))
        {
            issues.Add(new(true, "Bitte einen Ordner oder eine Datei zum Sichern auswählen."));
            return issues;
        }
        if (string.IsNullOrWhiteSpace(job.Target))
        {
            issues.Add(new(true, "Bitte einen Zielordner auf der Sicherungsfestplatte auswählen."));
            return issues;
        }
        if (!Path.IsPathFullyQualified(job.Source) || !Path.IsPathFullyQualified(job.Target))
        {
            issues.Add(new(true, "Quelle und Ziel müssen vollständige Pfade sein (z. B. C:\\Daten)."));
            return issues;
        }

        if (job.SourceIsFile ? !File.Exists(job.Source) : !Directory.Exists(job.Source))
            issues.Add(new(true, job.SourceIsFile ? "Die ausgewählte Datei existiert nicht." : "Der ausgewählte Ordner existiert nicht."));

        JobLayout layout;
        try { layout = JobLayout.For(job); }
        catch (Exception ex)
        {
            issues.Add(new(true, "Ungültiger Pfad: " + ex.Message));
            return issues;
        }

        if (layout.IsFile)
        {
            if (string.Equals(layout.TargetPath, layout.SourceDir, PathRules.Comparison))
                issues.Add(new(true, "Das Ziel darf nicht der Ordner sein, in dem die Datei liegt."));
        }
        else
        {
            if (PathRules.IsSameOrInside(layout.TargetPath, layout.SourceDir))
                issues.Add(new(true, "Das Ziel darf nicht im zu sichernden Ordner liegen."));
            else if (PathRules.IsSameOrInside(layout.SourceDir, layout.TargetPath))
                issues.Add(new(true, "Der zu sichernde Ordner darf nicht im Ziel liegen."));
        }

        foreach (var other in otherJobs)
        {
            if (other.Id == job.Id) continue;
            try
            {
                var o = JobLayout.For(other);
                bool clash = layout.IsFile || o.IsFile
                    ? string.Equals(layout.MirrorOf(layout.IsFile ? layout.Label : ""), o.MirrorOf(o.IsFile ? o.Label : ""), PathRules.Comparison)
                    : string.Equals(layout.MirrorRoot, o.MirrorRoot, PathRules.Comparison);
                if (clash)
                    issues.Add(new(true, $"Der Auftrag „{other.DisplayName}“ sichert bereits nach {o.MirrorRoot}. Bitte ein anderes Ziel wählen."));
            }
            catch { }
        }

        if (!layout.TargetDriveAvailable)
            issues.Add(new(false, "Das Ziellaufwerk ist gerade nicht angeschlossen. Die Sicherung startet, sobald es verfügbar ist."));
        else
        {
            var s = PathRules.DriveRoot(layout.SourceDir);
            var t = PathRules.DriveRoot(layout.TargetPath);
            if (s != null && t != null && string.Equals(s, t, StringComparison.OrdinalIgnoreCase))
                issues.Add(new(false, "Quelle und Ziel liegen auf demselben Laufwerk. Fällt die Festplatte aus, sind beide weg – besser eine andere Festplatte wählen."));
        }
        return issues;
    }
}
