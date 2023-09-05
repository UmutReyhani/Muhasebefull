using MongoDB.Driver;
namespace Muhasebe.Services
{
    public interface IConnectionService
    {
        IMongoDatabase db();
    }
}
