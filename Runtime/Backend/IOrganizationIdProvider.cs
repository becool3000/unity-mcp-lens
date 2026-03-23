namespace Unity.AI.Assistant.Backend
{
    interface IOrganizationIdProvider
    {
        bool GetOrganizationId(out string organizationId);
    }
}
