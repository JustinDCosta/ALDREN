namespace Aldren.Bench.Commands;

public static class ExceptionText
{
    /// <summary>The message of an exception and of each inner exception, outermost first.</summary>
    public static string Chain(Exception ex)
    {
        var messages = new List<string>();
        for (Exception? current = ex; current is not null; current = current.InnerException)
        {
            messages.Add(current.Message);
        }

        return string.Join(" → ", messages);
    }
}
