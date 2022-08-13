using System.Linq;
using System.Collections.Generic;


namespace SquareIntegration.Services
{
    public class SquareSettingsService : BaseEntityService<SquareSettings>
    {
        public SquareSettingsService(IRepository<SquareSettings> repository, IEventPublisher eventPublisher,
            INopDataProvider dataProvider, IStaticCacheManager staticCacheManager,
            ICacheKeyService cacheKeyService) : base(repository, eventPublisher, dataProvider, staticCacheManager,
            cacheKeyService)
        {
        }

        public SquareSettings GetSettingsCaCafe(int? caCafeId)
        {
            //get entries
            return Table.FirstOrDefault(_ => _.CaCafeId == caCafeId);
        }
        
        public IList<SquareSettings> GetAllSettings()
        {
            //get entries
            return GetAll().ToList();
        }
    }
}