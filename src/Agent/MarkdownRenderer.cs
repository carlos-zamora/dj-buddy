namespace DJBuddy.Agent;

/// <summary>
/// Stateful stream processor that converts inline Markdown formatting to VT sequences.
/// Feed characters in via <see cref="Process"/> as they arrive from the streaming response.
/// Currently handles <c>**bold**</c> → VT bold.
/// </summary>
internal sealed class MarkdownRenderer
{
    private const string Bold = "\x1b[1m";
    private const string Reset = "\x1b[0m";

    private int _asteriskCount;
    private bool _inBold;

    /// <summary>
    /// Processes a chunk of streamed text and returns the VT-rendered version.
    /// Call this for each delta from the streaming handler.
    /// </summary>
    public string Process(string chunk)
    {
        var sb = new System.Text.StringBuilder(chunk.Length + 16);

        foreach (var ch in chunk)
        {
            if (ch == '*')
            {
                _asteriskCount++;

                if (_asteriskCount == 2)
                {
                    // Toggle bold on/off.
                    _asteriskCount = 0;

                    if (_inBold)
                    {
                        sb.Append(Reset);
                        _inBold = false;
                    }
                    else
                    {
                        sb.Append(Bold);
                        _inBold = true;
                    }
                }

                continue;
            }

            // If we had a single '*', flush it as a literal before writing the current char.
            if (_asteriskCount > 0)
            {
                sb.Append('*', _asteriskCount);
                _asteriskCount = 0;
            }

            sb.Append(ch);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Flushes any pending state (e.g. a trailing asterisk) and resets for the next turn.
    /// Call after the full response has been received.
    /// </summary>
    public string Flush()
    {
        var sb = new System.Text.StringBuilder(8);

        if (_asteriskCount > 0)
        {
            sb.Append('*', _asteriskCount);
            _asteriskCount = 0;
        }

        if (_inBold)
        {
            sb.Append(Reset);
            _inBold = false;
        }

        return sb.ToString();
    }
}
