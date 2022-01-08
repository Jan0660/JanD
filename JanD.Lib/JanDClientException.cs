namespace JanD.Lib;

public class JanDClientException : Exception
{
    public JanDClientException(string message) : base(message.StartsWith("ERR:") ? message[4..] : message)
    {
    }
}