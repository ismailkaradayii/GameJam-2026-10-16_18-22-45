using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Ashfall.UI
{
    // Translates authored scene labels without changing the font, layout, or button bindings.
    [DefaultExecutionOrder(-100)]
    public sealed class EnglishUiText : MonoBehaviour
    {
    static readonly Dictionary<string, string> Exact = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase)
    {
        {"Yeni oyun", "New game"}, {"Ayarlar", "Settings"}, {"Nasıl oynanır", "How to play"}, {"Çıkıs", "Exit"}, {"Çıkış", "Exit"},
        {"Diğer", "Other"}, {"Silah", "Weapon"}, {"Çek / kaldır", "Draw / holster"}, {"Çömel", "Crouch"}, {"Şarjör", "Reload"},
        {"Su topu", "Water cannon"}, {"Fren / zıpla", "Brake / jump"}, {"Ateş / nişan", "Fire / aim"}, {"Tahliye", "Evacuation"},
        {"Bin / in", "Enter / exit"}, {"Hortum ve su", "Hose and water"}, {"Hareket", "Movement"}, {"Topla", "Retract"}, {"Koş", "Sprint"},
        {"Duraklat", "Pause"}, {"Hortum", "Hose"}, {"Pompa", "Pump"}, {"Hareket ve araç", "Movement and vehicle"},
        {"Volcano demo — kasabayı koru", "Volcano demo — defend the town"},
        {"Hazırlanmak için 120 saniyen var. Ardından köyü savun.", "You have 120 seconds to prepare. Then defend the village."},
        {"Yaya hareket / araç sürüşü. Fareyle etrafa bak.", "Move on foot or drive. Look around with the mouse."},
        {"Yaya halde koş.", "Sprint on foot."}, {"Araçta fren; yaya halde silah çekiliyken zıpla.", "Brake in the vehicle; jump on foot with the weapon drawn."},
        {"Sürücü kapısında E ile bin; durduktan sonra F ile in.", "Press E at the driver's door to enter. Press E to exit below 20 km/h."},
        {"Sürücü kapısında E ile bin; hız 20 km/sa altındayken E ile in.", "Press E at the driver's door to enter. Press E to exit below 20 km/h."},
        {"Aracın arkasında hortumu topla; sonra araca binebilirsin.", "Retract the hose at the rear before entering the truck."},
        {"Yaya halde sol tık ateş, sağ tık nişan al.", "On foot, left click to fire and right click to aim."},
        {"Arka hortum etkileşimi yokken şarjör değiştir.", "Reload when the rear hose interaction is unavailable."},
        {"Silah çekiliyken çömel / kalk.", "Crouch or stand with the weapon drawn."},
        {"Oyunu duraklat; Devam Et ile sürdür veya ana menüye dön.", "Pause the game; resume or return to the main menu."},
        {"Evlerin yakınında 'Herkes dışarıya!' diye seslen. Halkı sığınağa götür.", "Use E to recruit each marked group, assemble everyone, and lead them to shelter."},
        {"Araçla MAVİ göl halkasında dur. E bağlantıyı kurar; depo dolunca hortum otomatik toplanır.", "Stop in the BLUE lake parking bay and press F to connect. E exits the truck. The hose retracts when the tank is full."},
        {"Araçla MAVİ göl halkasında dur. P bağlantıyı kurar; E araçtan indirir. Depo dolunca hortum otomatik toplanır.", "Stop in the BLUE lake parking bay and press F to connect. E exits the truck. The hose retracts when the tank is full."},
        {"İstersen yaya halde aracın arkasından hortumu al, ucunu göle bırak ve P ile pompayı aç.", "On foot, take the hose from the rear of the truck, drop its intake in the lake, and press F to start the pump."},
        {"Araçtayken sağ tık basılı tut. Evleri söndür, lavları soğut ve ruhlara su sık. İşaret suyun tahmini çarpma noktasını gösterir; suyun hedefe ulaşmasını bekle.", "Hold right click in the truck to extinguish houses, cool lava, and spray spirits. The marker shows the predicted impact point; allow time for the water to reach it."},
        {"Komodindeki tabancayı E ile al. 1 silahı çek/kaldır, 0 silahı kaldır. Araçtan inince silah geri gelir.", "E: take the bedside pistol. 1: pistol, 2: empty hands, mouse wheel: switch. Exiting the truck draws your pistol."},
        {"Devam et", "Resume"}, {"ASHFALL  /  Bekleme noktası", "ASHFALL  /  PAUSED"}, {"Geri", "Back"}, {"Ana menüye dön", "Return to main menu"},
        {"Tam ekran", "Fullscreen"}, {"Grafik kalitesi", "Graphics quality"}, {"Yeniden dene", "Try again"}, {"Kasaba güvende", "Town secure"},
        {"ASHFALL  /  Görev", "ASHFALL  /  OBJECTIVE"}, {"Oyundan çık", "Quit game"}, {"Su deposu", "Water tank"}, {"Müzik", "Music"},
        {"Kurtarılan evler   0 / 5", "Houses saved   0 / 5"}, {"Kurtarılan ev  5 / 5", "Houses saved  5 / 5"},
        {"Beş ev de kurtarıldı. Tahliye edilen halk güvende.", "All five houses were saved. The evacuees are safe."},
        {"Kasaba seni bekliyor. Hazır olduğunda devam et.", "The town is waiting. Resume when you are ready."},
        {"Etkileşimler ve çevre sesleri", "Interactions and environmental sounds"}, {"Görev  ·  halkı tahliye et", "Objective  ·  evacuate the villagers"},
        {"Kamera", "Camera"}, {"Oyuncu", "Player"}, {"Oyun ve menü sesleri", "Game and menu sounds"}, {"Ses", "Audio"}, {"Görüntü", "Graphics"}, {"Can", "Health"},
        {"ASHFALL  /  Görev raporu", "ASHFALL  /  MISSION REPORT"}, {"Ses efektleri", "Sound effects"}, {"Yangın", "Fire"}, {"Hortum araçta.", "Hose stowed on truck."},
        {"Ortam ve müzik kategorisi", "Ambient and music audio"},
        {"Ses, görüntü ve kamera tercihlerini düzenle.", "Adjust audio, graphics and camera settings."}, {"Etkileşim", "Interaction"},
        {"W A S D ile yürü veya aracı sür. Shift ile koş, Space ile zıpla.", "Use WASD to move or drive. Shift to sprint; Space to jump."},
        {"Fareyi hareket ettirerek çevreye bak ve nişan al.", "Move the mouse to look around and aim."},
        {"Hortum ucunu suya bırak, P ile pompayı aç. R ile hortumu topla.", "Place the hose intake in water and press F to pump. Press R to retract it."},
        {"Araçtayken fareyle nişan al; sağ tık basılı tutarak su püskürt.", "In the truck, aim with the mouse and hold right click to spray water."},
        {"Lav akışlarından ve çöken evlerden uzak dur.", "Stay away from lava flows and collapsing houses."},
        {"Evlerin yanına yaklaş, Q ile seslen ve halkı SHELTER tabelalı sığınağa götür.", "Approach each marked group and press E. Bring them to the yellow assembly point, then lead everyone to the shelter."},
        {"E ile yakındaki kapı veya hortumla etkileş. F ile itfaiye aracına bin ya da in.", "Press E to interact, enter the truck, or exit below 20 km/h. Press F at the lake to refill."},
        {"E ile etkileş ve araca bin. Hız 20 km/sa altındayken E ile in. Göldeki su dolumu P.", "Press E to interact, enter the truck, or exit below 20 km/h. Press F at the lake to refill."},
        {"Q  HERKES DIŞARIYA!  ·  EN YAKIN EV: 212 m", "Q  CALL VILLAGERS  ·  NEAREST HOUSE: 212 m"},
        {"Görevi yeniden başlat", "Restart mission"}, {"Ana ses", "Master volume"}, {"Oyun duraklatıldı", "Game paused"}, {"Yaya", "On foot"},
        {"Kasaba durumu", "Town status"}, {"Fare hassasiyeti", "Mouse sensitivity"}, {"Su az · Gölden doldur", "Low water · Refill at lake"},
        {"Kasabayı tahliye et, suyu doldur ve evleri lavlardan koru.", "Evacuate the town, refill the tank and protect the houses from lava."},
        {"Yangın söndürülüyor", "Extinguishing fire"}, {"ESC  ·  Duraklat", "ESC  ·  Pause"},
        {"SIĞINAKTA  0 / 7     ÇAĞRILAN  0 / 7\nEvlerin yanına git ve Q ile halka seslen.", "SHELTERED  0 / 7     CALLED  0 / 7\nGo to the houses and call out with Q."}
    };

        Text label;
        string previous;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            Attach();
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Attach();

        static void Attach()
        {
            foreach (var text in Object.FindObjectsByType<Text>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (!text.TryGetComponent<EnglishUiText>(out _)) text.gameObject.AddComponent<EnglishUiText>();
        }

        void Awake() => label = GetComponent<Text>();

        void LateUpdate()
        {
            if (!label || label.text == previous) return;
            string translated;
            if (Exact.TryGetValue(label.text, out translated)) label.text = translated;
            else if (label.text.StartsWith("Yatak odasındaki tabancayı"))
                label.text = "Take the bedside pistol with E and activate the village alarm. Approach each marked group and press E, then bring them to the yellow assembly point. Press E there to leave each group. Once everyone has assembled, press E to lead them to the green shelter marker. Enter or exit the truck with E and fill its tank with F in the blue lake parking bay. Prepare within 120 seconds, then defend the village from fire spirits and burning lava.";
            label.text = label.text.Replace("ASHFALL", "VOLKANOVSKI");
            previous = label.text;
        }
    }
}

