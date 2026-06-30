namespace Solo;

public class ExistingInstanceActivationException : Exception
{
    public ExistingInstanceActivationException()
    {
    }

    public ExistingInstanceActivationException(string message)
        : base(message)
    {
    }

    public ExistingInstanceActivationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
