using ClinicNow.Model.Dto;
using ClinicNow.Services.Database.Entities;
using Mapster;

namespace ClinicNow.Services.Mapping;

public class NotificationMappingConfig : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<Notification, NotificationDto>();

        // NewsItem -> NewsItemDto: never map the raw bytes onto the list/detail
        // DTO (rulebook Part II §D) - only whether one exists.
        config.NewConfig<NewsItem, NewsItemDto>()
            .Map(dest => dest.HasImage, src => src.ImageData != null && src.ImageData.Length > 0)
            .Map(dest => dest.ImageUrl, src => src.ImageData != null && src.ImageData.Length > 0
                ? $"/api/NewsItem/{src.Id}/image"
                : null);
    }
}
