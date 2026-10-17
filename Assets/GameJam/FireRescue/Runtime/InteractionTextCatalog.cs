using UnityEngine;

namespace GameJam.FireRescue
{
    /// <summary>Shared presentation copy. It never changes mission state or invents an interaction.</summary>
    [CreateAssetMenu(menuName = "GameJam/Interaction text catalog")]
    public sealed class InteractionTextCatalog : ScriptableObject
    {
        public string takePistol = "Tabancayı Al";
        public string pistolSlot = "1. slota ekle";
        public string openDoor = "Kapıyı Aç", closeDoor = "Kapıyı Kapat";
        public string blockedDoor = "Kapı engellendi. Biraz geri çekil.";
        public string leaveHouse = "Evden Çık", takePistolFirst = "Çıkış görevi için önce tabancayı al";
        public string walkOutside = "Kapıdan dışarı yürü";
        public string enterTruck = "Araca Bin", exitTruck = "Araçtan İn";
        public string stopBeforeExit = "İnmek için hızı 20 km/sa altına düşür";
        public string drivingHint = "WASD ile sür · {0} su sık";
        public string clearExit = "Çıkış engellendi. Açık bir yere park et.";
        public string callVillagers = "Köylüleri Çağır", leadVillager = "Köylüyü Sığınağa Götür";
        public string leadToShelter = "Sığınağa götür", following = "Seni takip ediyor";
        public string callCoolingDown = "Sesini dinlendir. Birazdan tekrar çağırabilirsin.";
        public string shelter = "Sığınak", shelterAutomatic = "Köylüler içeri girince kurtarılır";
        public string fillTank = "Su Deposunu Doldur", fillingTank = "Depo Doluyor";
        public string disconnect = "Bağlantıyı kes", stopBeforeFill = "Dolum için mavi halkada dur";
        public string tankFull = "Depo Dolu", returnToVillage = "Köye dön";
        public string intakeOutOfWater = "Emiş ucu suya ulaşmıyor";
        public string hoseTooFar = "Aracı suya biraz daha yaklaştır";
        public string takeHose = "Emiş Hortumunu Al", dropHose = "Hortumu Bırak";
        public string putIntakeInWater = "Emiş ucunu suya bırak";
        public string retractHose = "Hortumu Topla", enablePump = "Pompayı Aç", disablePump = "Pompayı Kapat";
        public string extinguish = "Yangını Söndür", useWaterCannon = "Su topunu yangına doğrult ve basılı tut";
        public string pistolCollected = "Tabanca Alındı", residentSaved = "+1 Köylü Kurtarıldı!";
        public string objectiveComplete = "Görev Tamamlandı!", fireExtinguished = "Yangın Söndürüldü!";
        public string activateAlarm = "Köy Alarmını Çalıştır", alarmNeedsPistol = "Önce tabancayı al";
        public string alarmGetCloser = "Butona biraz daha yaklaş", alarmReloading = "Şarjör değişimi bitsin";
        public string alarmActivated = "Köy Uyarıldı!";
    }
}
