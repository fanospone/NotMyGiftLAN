# NOT MY GIFT! — LAN multiplayer

ASP.NET Core 8, SignalR, HTML/CSS/JavaScript. Room 3–8 pemain, host, reconnect, bluff, challenge, penalti, dan layar responsif.

## Persyaratan
- Visual Studio 2022 dengan workload **ASP.NET and web development** dan .NET 8 SDK.
- **Internet satu kali** untuk mengunduh JavaScript SignalR. Sesudah itu permainan dapat berjalan tanpa internet di LAN.

## Setup
1. Ekstrak ZIP dan buka `NotMyGiftLAN.csproj` di Visual Studio 2022.
2. Klik kanan `Setup-SignalR.ps1` → Run with PowerShell (atau dari PowerShell: `powershell -ExecutionPolicy Bypass -File .\Setup-SignalR.ps1`). Pastikan `wwwroot/lib/signalr.min.js` tersedia.
3. Jalankan dengan `dotnet run` di folder project, atau tombol Start di Visual Studio (profil project). Server HTTP: `http://0.0.0.0:5080`.
4. Di PC server, buka `http://localhost:5080`.
5. Jalankan `ipconfig` pada PC server. Temukan alamat IPv4 LAN, contoh `192.168.1.10`.
6. Di HP/laptop lain pada Wi-Fi yang sama, buka `http://192.168.1.10:5080` (ganti dengan IPv4 PC server). Jangan gunakan `localhost` dari HP.
7. Jika gagal terhubung, izinkan inbound TCP port **5080** pada Windows Defender Firewall untuk profil Private. Pastikan Wi-Fi tidak mengaktifkan AP/client isolation.

## Perintah firewall (PowerShell Administrator)
`New-NetFirewallRule -DisplayName "NOT MY GIFT LAN" -Direction Inbound -Protocol TCP -LocalPort 5080 -Action Allow -Profile Private`

## Aturan versi MVP
- Host membuat room, 3–8 pemain bergabung. Host memulai dan semua pemain menghafal satu kartu awal masing-masing.
- Host menutup kartu. Pemain pertama mengklaim satu barang saat memberikan kartu ke pemain berikutnya (boleh bluff).
- Penerima menerima atau menantang. Jika klaim benar, penantang +1 penalti; jika salah, pengirim +1 penalti.
- Penerima menyimpan kartu yang diberikan; kartu lama penerima dibuang. Pengirim mengambil kartu baru dari deck.
- Giliran berpindah ke penerima, hingga pemain mencapai 5 penalti atau deck habis. Penalti paling sedikit menang (seri dimungkinkan).
- Saat pemain terputus selama permainan, game menunggu pemain kembali dengan browser yang sama. Room disimpan di memori proses server, hilang ketika server restart.

## Catatan keamanan & batasan
- Cocok untuk jaringan LAN privat yang dipercaya, bukan deployment publik tanpa autentikasi dan rate limiting.
- Token pemain disimpan pada localStorage browser. Kode room bukan kata sandi.
- Kartu sebenarnya disimpan di server dan tidak dikirim pada fase permainan tertutup.
- Permainan membutuhkan koneksi SignalR; client JavaScript SignalR harus sudah diunduh melalui script setup.
- Karena environment pembuatan tidak memiliki .NET SDK, project belum dapat dikompilasi/diuji otomatis di sini.
