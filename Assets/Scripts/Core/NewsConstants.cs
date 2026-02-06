namespace Core
{
    /// <summary>
    /// Shared constants for the news system.
    /// </summary>
    public static class NewsConstants
    {
        // Media profile identifiers - single source of truth
        public const string MediaProfileFormal = "FORMAL";
        public const string MediaProfileSensational = "SENSATIONAL";
        public const string MediaProfileInvestigative = "INVESTIGATIVE";
        
        // Array for iteration
        public static readonly string[] AllMediaProfiles = 
        {
            MediaProfileFormal,
            MediaProfileSensational,
            MediaProfileInvestigative
        };
    }
}
