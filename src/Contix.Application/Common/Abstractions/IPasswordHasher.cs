namespace Contix.Application.Common.Abstractions;

public interface IPasswordHasher
{
    string Hash(string senha);
    bool Verify(string hash, string senha);
}
