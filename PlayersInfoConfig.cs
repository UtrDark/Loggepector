namespace Loggepector;

public static class PlayersInfoConfig
{
    public static (string Name, string Color, string[] Jobs)[] Departments { get; } = new (string Name, string Color, string[] Jobs)[]
    {
        ("COMMAND", "#3457B9", new[] { "Captain", "Head of Personnel", "Head of Security", "Chief Engineer", "Chief Medical Officer", "Research Director", "Quartermaster" }),
        ("SECURITY", "#BB3535", new[] { "Head of Security", "Security Officer", "Warden", "Detective", "Brig Physician" }),
        ("ENGINEERING", "#866500", new[] { "Chief Engineer", "Station Engineer", "Atmospheric Technician" }),
        ("MEDICAL", "#08768D", new[] { "Chief Medical Officer", "Medical Doctor", "Paramedic", "Chemist", "Virologist", "Psychologist" }),
        ("SCIENCE", "#8051AA", new[] { "Research Director", "Scientist", "Roboticist", "Geneticist" }),
        ("SUPPLY", "#925F19", new[] { "Quartermaster", "Cargo Technician", "Shaft Miner", "Bitrunner" }),
        ("SERVICE", "#3E792C", new[] { "Head of Personnel", "Assistant", "Bartender", "Botanist", "Cook", "Janitor", "Clown", "Mime", "Chaplain", "Curator", "Lawyer" }),
        ("SILICON", "#596577", new[] { "AI", "Cyborg" })
    };
}
