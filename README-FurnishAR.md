# Furnish AR — kullanım ve doğrulama

Unity 6000.6.7f1 · AR Foundation / ARCore 6.6.2 · Android ARM64, IL2CPP · sürüm 1.1.0 (7).

## Telefonda kullanım

1. ARCore uyumlu Android telefona `Builds/FurnishAR-v7.apk` dosyasını kurun ve kamera izni verin.
2. Kamerayı yavaşça zemine doğru gezdirin. Halka görünmesi o noktada izlenen yatay yüzey bulunduğunu belirtir.
3. Katalogdan mobilya seçin. Katalog küçük bir seçim çubuğuna dönüşür. Kamera alanında algılanan zemine dokunup bırakın.
4. Başka ürün için alttaki **Değiştir +** alanını açın. Önceki mobilyalar korunur.
5. Mobilyaya dokunarak seçin; sürükleyerek taşıyın. Sola/Sağa düğmeleri veya iki parmak hareketiyle döndürün. **Sil** yalnızca seçili mobilyayı siler; **Bitti** seçimi kaldırır.
6. Seçili mobilya varken boş alana ilk dokunuş seçimi kaldırır. Sonraki dokunuş yeni mobilya yerleştirir.

## Yapılan düzeltmeler

- Kamera ve katalog örtüşmesine göre mobilyaları 8 metreye kadar iten ve kamera önüne yeniden taşıyan davranış kaldırıldı. Konum yalnızca kullanıcı hareketi veya AR takip düzeltmesiyle değişir.
- Kamera pozisyon/dönüş input action'ları `Value` tipinde ve takip durumu girdisiyle yapılandırıldı. ARInputManager ve ARAnchorManager doğrulandı.
- Yerleştirme sadece `PlaneWithinPolygon`, `HorizontalUp`, `Tracking` yüzeylerde yapılır. İzleme kaybolduğunda yeni yerleştirme/taşıma durur; tahmini kamera önü konumu üretilmez.
- Modelden bağımsız, zemin seviyesinde bir kök oluşturulur. Modelin gerçek geometri sınırları bu köke ortalanır; dekoratif temas gölgesi ölçüme dahil edilmez.
- Gerçek AR anchor yaşam döngüsü eklendi. Taşıma başında bağlantı çözülür, taşıma sonunda yenilenir; geç dönen istekler ve silinen nesneler temizlenir.
- Kaynak prefab ölçeği korunur. Paket modelleri zaten metre ölçeğindedir: örneğin iki kanepe yaklaşık 1,811 m ve 2,403 m genişliktedir. Bunlar paket geometrisinin ölçüleridir; üretici ürün ölçüsü iddiası değildir.
- UI dokunuşları, sürükleme ve çoklu dokunma yerleştirmeden ayrıldı. Yerleştirme kısa dokunuşun bırakılmasında gerçekleşir. UI üzerinde başlayan hareket mobilya üretmez.
- Türkçe kartlar, gerçek paket önizlemeleri, güvenli ekran alanı, küçülen katalog ve seçim kontrolleri kullanılır.
- Halka nişangâhındaki SmoothStep/alfa hesabı düzeltildi.
- Editörde 3D modellerin üstünü kapatan webcam overlay kaldırıldı. Editör simülasyonu bir sanal zemindir; gerçek dünya AR testi değildir.

## Varlıklar ve sahne

- [Ana sahne](Assets/FurnishAR/Scenes/FurnishARScene.unity)
- [Gerçek kaynak paket](<Assets/NextGen Furniture Pack>) — 15 prefab; uydurma model kullanılmaz.
- [Katalog](Assets/FurnishAR/Resources/FurnitureCatalog.asset)
- [Yerleştirme](Assets/FurnishAR/Scripts/Core/ARPlacementManager.cs)
- [Mobilya ve anchor yaşam döngüsü](Assets/FurnishAR/Scripts/Core/PlacedFurniture.cs)
- [Etkileşim](Assets/FurnishAR/Scripts/Core/FurnitureInteractionManager.cs)
- [Arayüz](Assets/FurnishAR/Scripts/UI/AppRuntimeUI.cs)

## Doğrulama

`FurnishAR > Validate Model Dimensions` gerçek prefab/önizleme referanslarını ve ölçülerini denetler. Sonuç: [model ölçüleri](Builds/ModelAudit.txt).

Play modunda `FurnishAR > Run Play Mode Checks` 15 modeli oluşturur; ölçek, zemine hizalanma, kamera hareketinde dünya konumu, seçme, döndürme, taşıma, seçim kaldırma, silme, birden fazla ürün ve alt UI korumasını denetler. Sonuç: [Play Mode kontrolleri](Builds/PlayModeChecks.txt). Test, gerçek dokunmatik cihazı veya ARCore sağlayıcısını taklit etmez.

Editör kamera kontrolleri: WASD hareket, sağ fare tuşu ile bakış, tekerlek ile seçili mobilyayı döndürme.

Bilgisayara bağlı Android cihaz bulunmadığından fiziksel kamera takibi, anchor relocalization, gerçek odada metre doğruluğu, kamera izin akışı ve cihaz performansı henüz cihaz üzerinde doğrulanmadı. Telefon kabul testi: iki farklı mobilya yerleştirin, çevrelerinde yürüyün, uygulamayı arka plana alıp geri dönün, birini taşıyıp döndürün ve silin; diğerinin konumunun korunduğunu kontrol edin. Mobilyalar uygulama kapanınca kaydedilmez.

Android APK derlemesi başarıyla tamamlandı: 0 hata, 366 uyarı (çoğunlukla paket shader uyarıları ve eski API kullanımları). APK boyutu: 56.551.778 bayt (~54 MiB). Editör Play Mode kontrolünde uygulama çalışma zamanı hatası görülmedi.
