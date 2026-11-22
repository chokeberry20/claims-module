namespace ClaimsModule.Persistence.Conventions;

/// <summary>
/// The column conventions from S15.1, kept out of the domain model as EF shadow properties (D-48).
/// Interceptors set them; global query filters read them.
/// </summary>
public static class ShadowProperties
{
    public const string OrganisationId = "OrganisationId";
    public const string CreatedAt = "CreatedAt";
    public const string UpdatedAt = "UpdatedAt";
    public const string UserCreated = "UserCreated";
    public const string UserModified = "UserModified";
    public const string IsDeleted = "IsDeleted";
    public const string DeletedAt = "DeletedAt";
    public const string RowVersion = "RowVer";
}
