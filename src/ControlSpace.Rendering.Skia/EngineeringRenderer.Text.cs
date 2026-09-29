using SkiaSharp;
namespace ControlSpace.Rendering.Skia;

public sealed partial class EngineeringRenderer
{
    private readonly record struct RunKey(string Text, float Size, bool Bold, float Width);
    private sealed record TextRun(SKTextBlob? Blob, string Text, float Width);
    private readonly Dictionary<RunKey, TextRun> _textRuns = [];
    private readonly Queue<RunKey> _textOrder = [];
    public int CachedTextRunCount => _textRuns.Count;
    public long TextRunCreations { get; private set; }
    private TextRun Run(string text, float size, bool bold, float maxWidth = 0)
    {
        text ??= "";
        // Rendering limits only; the complete text remains available in the document/dialog.
        if (text.Length > 512) { int end = char.IsHighSurrogate(text[510]) ? 510 : 511; text = text[..end] + "…"; }
        var key = new RunKey(text, size, bold, maxWidth);
        if (_textRuns.TryGetValue(key, out var cached)) return cached;
        var font = bold ? _bold : _font; font.Size = size;
        string fitted = text; float width = font.MeasureText(fitted);
        if (maxWidth > 0 && width > maxWidth)
        {
            int lo = 0, hi = fitted.Length;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (font.MeasureText(fitted[..mid] + "…") <= maxWidth) lo = mid; else hi = mid - 1;
            }
            // Never split a UTF-16 surrogate pair while shortening a display label.
            if (lo > 0 && char.IsHighSurrogate(fitted[lo - 1])) lo--;
            fitted = fitted[..lo] + "…"; width = font.MeasureText(fitted);
        }
        var run = new TextRun(SKTextBlob.Create(fitted, font), fitted, width); TextRunCreations++;
        if (_textRuns.Count == 512)
        {
            var oldest = _textOrder.Dequeue(); _textRuns[oldest].Blob?.Dispose(); _textRuns.Remove(oldest);
        }
        _textRuns.Add(key, run); _textOrder.Enqueue(key); return run;
    }
    private void PaintRun(SKCanvas canvas, TextRun run, float x, float baseline, SKColor color, float size, bool bold)
    {
        Color(color);
        if (run.Blob is not null) canvas.DrawText(run.Blob, x, baseline, _paint);
        else { var font = bold ? _bold : _font; font.Size = size; canvas.DrawText(run.Text, x, baseline, font, _paint); }
    }
    private void CenterText(SKCanvas canvas, string text, float center, float baseline, float width, SKColor color, float size)
    {
        var run = Run(text, size, false, width);
        PaintRun(canvas, run, center - run.Width / 2, baseline, color, size, false);
    }
    private void ClearTextRuns()
    {
        foreach (var run in _textRuns.Values) run.Blob?.Dispose(); _textRuns.Clear(); _textOrder.Clear();
    }
}
