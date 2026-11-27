namespace Tests.Exceptions;

public class DuplicateGroupByExceptionTests
{
    private const string DEFAULT_MESSAGE = "Duplicate use of GroupBy(). Ensure you don't use GroupBy() more than once in the same specification!";

    [Fact]
    public void ThrowWithDefaultConstructor()
    {
        Action sut = () => throw new DuplicateGroupByException();

        sut.Should().Throw<DuplicateGroupByException>().WithMessage(DEFAULT_MESSAGE);
    }

    [Fact]
    public void ThrowWithInnerException()
    {
        var inner = new Exception("test");
        Action sut = () => throw new DuplicateGroupByException(inner);

        sut.Should().Throw<DuplicateGroupByException>()
            .WithMessage(DEFAULT_MESSAGE)
            .WithInnerException<Exception>()
            .WithMessage("test");
    }
}
