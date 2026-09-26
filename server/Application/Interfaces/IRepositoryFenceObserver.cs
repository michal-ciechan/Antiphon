namespace Antiphon.Server.Application.Interfaces;

/// <summary>CARD-0726 D-8: a journal fence refused a mutation lease. The argument is the common directory.</summary>
public interface IRepositoryFenceObserver
{
    void Fenced(string commonDirectory);
}
