using Jellyfin.Database.Implementations.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jellyfin.Database.Implementations.ModelConfiguration
{
    /// <summary>
    /// FluentAPI configuration for the IFramePlaylistInfo entity.
    /// </summary>
    public class IFramePlaylistInfoConfiguration : IEntityTypeConfiguration<IFramePlaylistInfo>
    {
        /// <inheritdoc/>
        public void Configure(EntityTypeBuilder<IFramePlaylistInfo> builder)
        {
            builder.HasKey(info => info.ItemId);
        }
    }
}
