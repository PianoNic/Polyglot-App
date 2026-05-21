using Polyglot.Application.Dtos;
using Polyglot.Domain;

namespace Polyglot.Application.Mappers
{
    public static class ModelListEntryMapper
    {
        public static ModelListEntryDto ToDto(this ModelListEntry entry)
        {
            return new ModelListEntryDto
            {
                Id = entry.Id,
                ModelId = entry.ModelId,
                ListType = entry.ListType,
            };
        }
    }
}
