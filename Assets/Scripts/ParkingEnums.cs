public enum RegionType
{
    Inner = 0,
    Outer = 1,
    Far = 2
}

public enum ParkingType
{
    Legal = 0,
    Illegal = 1
}

public enum ParkingAreaKind
{
    NormalParkingSpot = 0,
    NoParkingZone = 1
}

public enum StreetSide
{
    North = 0,
    South = 1,
    East = 2,
    West = 3,
    Unknown = 4
}

public enum StreetNameOption
{
    Heydarkhani = 0,
    ShahidMalekloo = 1,
    ShahidNilforooshan = 2,
    ElmOSanat = 3,
    Abgoon41 = 4,
    Zolfaghari = 5,
    Lotfizadeh = 6,
    Saadatmand = 7,
    Layeghnia = 8,
    Mehran = 9,
    Bagheri = 10,
    Salehi = 11,
    Attari42 = 12,
    Ebadi = 13,
    Vahdat = 14,
    Shohada = 15

    // Later add new street names ONLY at the end.
    // Example:
    // NewStreetName = 16
}

public enum StreetCodeOption
{
    HDR = 0,
    MAL = 1,
    NIL = 2,
    ELM = 3,
    ABG41 = 4,
    ZOL = 5,
    LOT = 6,
    SAA = 7,
    LAY = 8,
    MEH = 9,
    BAG = 10,
    SAL = 11,
    ATT42 = 12,
    EBA = 13,
    VAH = 14,
    SHO = 15

    // Later add new street codes ONLY at the end.
    // Example:
    // NEW = 16
}

public enum IllegalReason
{
    None = 0,

    DrivewayEntrance = 1,
    NoParkingSign = 2,
    NoStoppingSign = 3,
    IntersectionCorner = 4,
    PedestrianCrossing = 5,
    BusStop = 6,

    Other = 99
}

public enum LegalParkingFeature
{
    None = 0,
    SmartParking = 1,      // پارکینگ هوشمند
    DisabledParking = 2,   // ویژه معلولین
    Other = 99
}
