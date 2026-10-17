# VOLKANOVSKİ / ASHFALL ses denetimi — 17 Ekim 2026

Değişiklikler mevcut projeye ve sahnelere uygulandı. Yeni AudioManager veya AudioMixer oluşturulmadı. Orijinal kayıtlar korundu. Testler Unity 6000.0.58f2 Editor Play Mode içinde çalıştırıldı; hoparlör/kulaklıkla dinleme yapıldığı iddia edilmiyor.

## Bulunan ve düzeltilen hatalar

| Önem | Sorun / neden | Yapılan düzeltme |
|---|---|---|
| İşlevsel | Yürüme bileşeni ve AudioSource kapatılmıştı. Koşu alanına da yürüyüş kaydı atanıyordu. | Gerçek `walksound.mp3` ve koşu kaydından hazırlanan ayrı loop bağlandı, bileşenler açıldı. |
| İşlevsel | `FinalLoopSetup.RemoveUnwantedAudio`, sonraki kurulumlarda hareket seslerini tekrar kapatıyordu. | Yalnız eski seslenme kaydını temizleyen bölüme dönüştürüldü; hareket seslerine dokunmuyor. |
| İşlevsel | Kısa adımlarda AudioSource.volume sıfır kalabiliyordu. | One-shot oynatımı için kaynak seviyesi düzeltilip yürüme/koşu geçişinde önceki kayıt durduruldu. Gerçek hız, grounded, sürüş ve askıya alma koşulları korundu. |
| İşlevsel | Acı kaynağı kurulumda sıfır seviyeliydi; lavdan çıkınca mevcut one-shot devam ediyordu. | Çalışma anında volume=1, loop=false; yalnız başarılı `TakeHeatDamage` sonrasında çalma; hasarsız temas/askıya alma/devre dışı kalmada durdurma; kayıt bitmeden tekrar başlatmama. Hasar miktarı değiştirilmedi. |
| İşlevsel | Lav akışı eski `volkanbubbling.wav` kaydını kullanıyordu ve kaynak rota başlangıcında kalıyordu. | `lavakaynar.wav` kaynaklı loop bağlandı. Her akışın tek kaynaklı `Lava Flow Audio` çocuğu görünür lav cephesini takip ediyor. Önceki bağlı kök kaynak kaldırıldı. |
| İşlevsel | Ateş ruhlarına `alevruhu.wav` oynatıcısı bağlanmamıştı. | Her canlı aktörde tek 3D kaynak; yenilme, disable ve yeniden kullanım için yaşam döngüsü kontrolü. Sahnedeki aktörlere ve bunlardan üretilen klonlara gerçek kayıt bağlandı. |
| Çakışma | Ruh mermi darbesinde hem aktör efekt sistemi hem silah geri bildirimi darbe sesi üretiyordu. | Mekânsal darbenin sahibi aktör efekt sistemi oldu; gerçek `çarpmases.mp3` override'ı bağlandı. Silahın ayrı HUD isabet onayı korundu. Metal yüzeylerde mevcut tek darbe kanalı kullanılıyor. |
| Çakışma | Kalabalık kaynağı Pause kontrolünden önce yeniden başlatılabiliyordu. | Pause/UnPause işlemi oynatma kararından önce alındı. Tek kalabalık kaynağı ve NPC takip mantığı korundu. |
| İyileştirme | Ortak yangın sesi Pause sırasında devam ediyordu. | Duraklatma ve devam etme eklendi. Bir ev söndüğünde diğer yanan evlerin sesi korunuyor. |
| İşlevsel | Silah, bazı yüzey/kovan ve savaş efektleri SFX tercihini atlıyordu. | Oynatma seviyeleri mevcut `Ashfall.SfxVolume` ayarına bağlandı. Master hâlâ mevcut AudioListener ayarıyla yönetiliyor. |
| İyileştirme | Menü geri bildirimi `PlayClipAtPoint` ile 3D üretiliyordu. | Menü sesleri kısa ömürlü 2D kaynaklar kullanıyor; mevcut buton işlevlerine ek dinleyici çoğaltılmadı. Kapalı geri bildirim bileşeni hover/click üretmiyor. |
| İşlevsel | Video yalnız Start içinde hazırlanıyordu; bileşen kapatılıp açıldığında hazırlık/loop coroutine durumu güvenilir değildi. | Disable'da callback/coroutine/oynatım temizliği; tekrar etkinleşmede yeniden Prepare. Panel açılışı video nesnesini kapatmadığı için Settings/How To Play videoyu yeniden başlatmıyor. |
| İşlevsel | Görünen görev sayacı ile sekansın ayrı görsel uyarı sayacı farklıydı; ses görünen sıfırdan sonra başlayabiliyordu. | Görev sayacında iki saniye sınırına ses başlangıcı, `TriggerDeadline` sıfır olayına sesin dört saniyelik son süresi bağlandı. Sonraki görsel patlama bildirimi aynı sesi veya son süreyi tekrar başlatmıyor. |

## Patlama, araç ve hortum

- Mevcut patlama mekaniği yeniden yazılmadı. `VolcanoMissionDirector` görünen görev sayacında iki saniye sınırını geçtiğinde sesi başlatıyor; sıfırdaki mevcut `TriggerDeadline` olayı dört saniyelik son süreyi başlatıyor. Yönetici bulunmayan bağımsız sekanslarda eski uyarı süresi bağlantısı yedek olarak korunuyor. Mevcut 0,8 saniye fade ardından rüzgâr fade-in'i korunuyor. Patlama sırasında SFX değişikliklerinin de uygulanması sağlandı. Sinematik, görsel uyarı ve lav hareket süreleri değiştirilmedi.
- Araç kaynağı ölçülen Rigidbody hızı ve dolu sürücü koltuğuna bağlı. Durma ve araçtan çıkış kontrolleri geçti; sürüş fiziği değiştirilmedi.
- Hortum mevcut `IsSpraying` verisini kullanıyor. Bırakma ve boş depo testleri geçti; su tüketimi ve söndürme mantığı değiştirilmedi.
- Eski volkan kaydı dosya olarak korunuyor ancak akışlarda ikinci bir katman olarak bağlanmıyor. Kaynağı/olayı olmayan bell, alternatif crowd, alternatif metal vb. kayıtlar rastgele olaylara atanmadı.
- Tam adı `gunshot`, `impact` veya `bullet` olan yeni bir dosya uydurulmadı. Gerçek silah kayıtları `pistol-shot-01/02/03.wav`, mevcut yüzey kayıtları `impact-metal/wood/dirt/concrete.wav`.

## Kayıt hazırlığı ve envanter

- Koşu orijinali: 43,189 saniye stereo, 44,1 kHz. Son bölümü belirgin biçimde sönüyor. 1–23 saniye aralığından 120 ms birleşim geçişli `Assets/Art/sesdosya/Loops/BreathingRunningLoop.wav` üretildi; çıktı 21,880 saniye. Nefes kaydı hızlandırılmıyor.
- Lav orijinali: 26,099 saniye stereo, 44,1 kHz; giriş/sonda sessiz bölgeler var. 2–23,5 saniye aralığından 160 ms birleşim geçişli `Assets/Art/sesdosya/Loops/LavaFlowLoop.wav` üretildi; çıktı 21,340 saniye. Kayıttaki doğal köpürme aralıkları korundu.
- Kesme ve PCM birleşim geçişi uygulanmıştır; yeni ses sentezlenmedi. İşitsel loop kalitesi ayrıca dinlenmeli.
- `Logs/AudioAssetInventory.md`: tüm 49 AudioClip'in gerçek yolu, süresi, kanal/frekans bilgisi ve doğrudan sahne/prefab referansları. Resources üzerinden yüklenen savaş kayıtları ayrıca açıklanıyor.
- `Logs/AudioWaveMetadata.txt` ve `Logs/AudioLoopPreparation.txt`: kayıt incelemesi ve kopyaların üretim parametreleri.

## Değiştirilen dosyalar ve nesneler

### Runtime scriptleri

- `Assets/GameJam/FireRescue/Runtime/FireRescueMovementAudio.cs`
- `Assets/GameJam/FireRescue/Runtime/FireAudioAmbience.cs`
- `Assets/GameJam/FireRescue/Runtime/WindAudioPulse.cs`
- `Assets/GameJam/FireRescue/Runtime/VolcanoMissionDirector.cs`
- `Assets/GameJam/FireRescue/Runtime/VolcanoEruptionSequence.cs`
- `Assets/GameJam/FireRescue/Runtime/LavaPlayerContact.cs`
- `Assets/GameJam/FireRescue/Runtime/EvacuationMission.cs`
- `Assets/GameJam/FireRescue/Runtime/ProgressiveLavaFlow.cs`
- `Assets/GameJam/FireRescue/Runtime/LavaSpiritAudio.cs` (yeni)
- `Assets/GameJam/FireRescue/Runtime/LavaCreatureActor.cs`
- `Assets/GameJam/FireRescue/Runtime/LavaCombatEffects.cs`
- `Assets/Itfaiye/Scripts/ItfaiyePistolFeedback.cs`
- `Assets/Itfaiye/Scripts/ItfaiyePistolCasings.cs`
- `Assets/Scripts/MainMenu/AshfallMenuAudioFeedback.cs`
- `Assets/Scripts/MainMenu/AshfallVideoBackgroundController.cs`

### Kurulum ve test araçları

- `Assets/GameJam/FireRescue/Editor/AudioIntegrationSetup.cs`
- `Assets/GameJam/FireRescue/Editor/FinalLoopSetup.cs`
- `Assets/GameJam/FireRescue/Editor/PaceAudioChecks.cs`
- `Assets/GameJam/FireRescue/Editor/AudioSystemAudit.cs` (yeni)

### Sahne ve ses varlıkları

- `Assets/Scenes/MainMenu.unity`: mevcut menü ses bağlantıları ve sessiz VideoPlayer ayarları.
- `Assets/BK/PureNature_Volcano/Scenes/Volcano_Demo.unity`: yürüyüş/koşu bağlantıları, lav akışlarının 23 `Lava Flow Audio` çocuğu, mevcut ruh aktörlerinde ses bileşeni/darbe referansı.
- `Assets/Scenes/İtfaiye aracı.unity`: ayrı yürüyüş/koşu referansları.
- İki loop WAV ve Unity `.meta` dosyaları. Mevcut araç ve oyuncu prefab bağlantıları yeniden kontrol edilip korunmuştur; gereksiz yeni prefab oluşturulmamıştır.

## Gerçekten çalıştırılan testler

| Rapor | Sonuç / kapsam |
|---|---|
| `Logs/PaceAudioChecks.txt` | 20 PASS, COMPLETED. Gerçek CharacterController ile geçici düz zeminde yürüme/koşu/idle; animasyonların korunması; gerçek WheelCollider sürüş, fren, araçtan çıkış ve motor durması. |
| `Logs/AudioTimelineChecks_Volcano_Demo.txt` | 22 PASS, COMPLETED. Patlama/rüzgâr komponent zamanlaması ve reset/disable, ruh kaynağı yeniden kullanım, gerçek metal raycast darbesi/ıskalama, bağımsız iki evin ortak yangın sesi, gerçek su emisyonu/bırakma/boş depo. |
| `Logs/AudioTimelineChecks_MainMenu.txt` | 14 PASS, COMPLETED. Ortak zamanlama/ruh testlerine ek olarak VideoPlayer hazırlanmış ve kare üretiyor; video audio output None. Menü Settings butonu ayrıca gerçek UI tıklamasıyla açıldı. |
| `Logs/AudioLavaContactChecks.txt` | 12 PASS, COMPLETED. Gerçek lav mesh'i, 18 hasarlık temas, acı kaynağının çalması; suyla yerel soğutma ve hasarsız temasta sesin hemen durması; ilgili yangın/soğutma mekanik kontrolleri. |

Bu raporlardaki bazı kontroller ortaktır; sayılar benzersiz test toplamı olarak sunulmuyor. Kontroller kaynak durumunu ve gerçek bileşen davranışını inceler, kulaklıkla dinleme yerine geçmez. Geçici test nesneleri ve testte değişen oyun verileri sahnelere kaydedilmedi. Son script derlemesi tamamlandı. Onarım sırasında görülen Unity null-coalescing/fake-null kaynak oluşturma hatası düzeltildi; son testlerde tekrarlanmadı.

## Kalan manuel kontroller / Inspector

- Kulaklıkla ses dengesi, farklı mesafelerde 3D duyulurluk ve kayıtların algısal döngü kalitesi kontrol edilmeli. Özellikle `walksound.mp3` kaydının içerik kalitesi dinleyerek onaylanmadı.
- Tam NPC tahliye rotası ve Pause/Resume kalabalık geçişi uçtan uca oynanmadı; bu bölümün sayım, tek kaynak ve yaşam döngüsü kodu denetlendi.
- Gerçek girişle duvara dayanma, zıplama/iniş, tüm köyü kapsayan lav rotaları ve tam sahne-yeniden-yükleme döngüsü ayrıca oynanmalı. Hareket sesi ölçülen hız/grounded durumuna bağlı kalıyor; bu senaryoların hepsi Play Mode'da tek tek denenmiş gibi sunulmuyor.
- Zorunlu manuel Inspector ataması kalmadı. Volkan akışında `bubblingClip` yeni lav loop'una, hareket sesinde `walkingClip` gerçek yürüyüş kaydına ve `runningClip` koşu loop'una bağlı. Ruh aktörleri `alevruhu.wav` ve tek darbe kaydına bağlı.
- Tekrar kontrol için: `Volcano_Demo` Edit Mode'da **GameJam > Final Loop > Run speed audio checks**. Geçici test düzlemi kullanır ve bitince Play Mode'dan çıkar. Ayrı lav testi için aynı sahnede Play Mode'a girip **ASHFALL > Audio > Run lava contact checks (Play Mode)** seçilir; testten sonra Play Mode durdurulur.
