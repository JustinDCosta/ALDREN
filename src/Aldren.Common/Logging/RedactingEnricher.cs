using Serilog.Core;
using Serilog.Events;

namespace Aldren.Common.Logging;

/// <summary>Runs every string property of every event, nested ones included, through <see cref="Redactor"/>.</summary>
public sealed class RedactingEnricher : ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        foreach (var (name, value) in logEvent.Properties.ToList())
        {
            var redacted = Redact(value);
            if (!ReferenceEquals(redacted, value))
            {
                logEvent.AddOrUpdateProperty(new LogEventProperty(name, redacted));
            }
        }
    }

    private static LogEventPropertyValue Redact(LogEventPropertyValue value)
    {
        switch (value)
        {
            case ScalarValue { Value: string or Exception or Uri } scalar:
                var text = scalar.Value.ToString()!;
                var masked = Redactor.Redact(text);
                return masked == text ? value : new ScalarValue(masked);
            case SequenceValue sequence:
                return new SequenceValue(sequence.Elements.Select(Redact));
            case StructureValue structure:
                return new StructureValue(
                    structure.Properties.Select(p => new LogEventProperty(p.Name, Redact(p.Value))), structure.TypeTag);
            case DictionaryValue dictionary:
                return new DictionaryValue(dictionary.Elements.Select(e => KeyValuePair.Create(e.Key, Redact(e.Value))));
            default:
                return value;
        }
    }
}
