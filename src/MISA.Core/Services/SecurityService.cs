using System.Security.Cryptography;
using System.Text;
using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.IdentityModel.Tokens;

namespace MISA.Core.Services
{
    public class SecurityService
    {
        private readonly LoggingService _loggingService;
        private readonly Dictionary<string, string> _deviceKeys;
        private readonly byte[] _encryptionKey;
        private readonly byte[] _iv;
        private readonly JwtSecurityTokenHandler _tokenHandler;

        private const int KEY_SIZE = 256;
        private const int IV_SIZE = 16;
        private const int SALT_SIZE = 16;
        private const int ITERATIONS = 10000;

        public SecurityService()
        {
            _loggingService = new LoggingService();
            _deviceKeys = new Dictionary<string, string>();
            _tokenHandler = new JwtSecurityTokenHandler();
            _encryptionKey = new byte[KEY_SIZE / 8];
            _iv = new byte[IV_SIZE];

            GenerateEncryptionKey();
        }

        public async Task InitializeAsync()
        {
            try
            {
                _loggingService.LogInformation("Initializing Security Service...");

                // Load existing device keys or create new ones
                await LoadDeviceKeysAsync();

                // Generate new encryption key if needed
                if (!await HasValidEncryptionKeyAsync())
                {
                    await GenerateAndSaveEncryptionKeyAsync();
                }

                _loggingService.LogInformation("Security Service initialized successfully");
            }
            catch (Exception ex)
            {
                _loggingService.LogError(ex, "Failed to initialize Security Service");
                throw;
            }
        }

        private void GenerateEncryptionKey()
        {
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(_encryptionKey);
            rng.GetBytes(_iv);
        }

        private async Task LoadDeviceKeysAsync()
        {
            try
            {
                var keysFile = "data/device_keys.json";
                if (File.Exists(keysFile))
                {
                    var json = await File.ReadAllTextAsync(keysFile);
                    var loadedKeys = JsonConvert.DeserializeObject<Dictionary<string, string>>(json);
                    if (loadedKeys != null)
                    {
                        foreach (var kvp in loadedKeys)
                        {
                            _deviceKeys[kvp.Key] = kvp.Value;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _loggingService.LogWarning(ex, "Failed to load device keys, starting fresh");
            }
        }

        private async Task<bool> HasValidEncryptionKeyAsync()
        {
            try
            {
                var keyFile = "data/encryption.key";
                if (!File.Exists(keyFile))
                    return false;

                var keyData = await File.ReadAllBytesAsync(keyFile);
                return keyData.Length == _encryptionKey.Length;
            }
            catch
            {
                return false;
            }
        }

        private async Task GenerateAndSaveEncryptionKeyAsync()
        {
            try
            {
                Directory.CreateDirectory("data");
                var keyFile = "data/encryption.key";

                using var rng = RandomNumberGenerator.Create();
                rng.GetBytes(_encryptionKey);

                await File.WriteAllBytesAsync(keyFile, _encryptionKey);
                _loggingService.LogInformation("New encryption key generated and saved");
            }
            catch (Exception ex)
            {
                _loggingService.LogError(ex, "Failed to generate encryption key");
                throw;
            }
        }

        public string GenerateDeviceId()
        {
            return Guid.NewGuid().ToString("N")[..16];
        }

        public string GenerateDeviceKey(string deviceId)
        {
            var key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            _deviceKeys[deviceId] = key;
            _ = Task.Run(() => SaveDeviceKeysAsync()); // Fire and forget
            return key;
        }

        public bool ValidateDeviceSignature(string deviceId, string signature)
        {
            try
            {
                if (!_deviceKeys.ContainsKey(deviceId))
                    return false;

                var expectedKey = _deviceKeys[deviceId];
                return signature == expectedKey;
            }
            catch (Exception ex)
            {
                _loggingService.LogError(ex, $"Failed to validate device signature for {deviceId}");
                return false;
            }
        }

        private async Task SaveDeviceKeysAsync()
        {
            try
            {
                Directory.CreateDirectory("data");
                var keysFile = "data/device_keys.json";
                var json = JsonConvert.SerializeObject(_deviceKeys, Formatting.Indented);
                await File.WriteAllTextAsync(keysFile, json);
            }
            catch (Exception ex)
            {
                _loggingService.LogError(ex, "Failed to save device keys");
            }
        }

        public string Encrypt(string plainText)
        {
            try
            {
                using var aes = Aes.Create();
                aes.Key = _encryptionKey;
                aes.GenerateIV();

                using var encryptor = aes.CreateEncryptor();
                using var msEncrypt = new MemoryStream();
                using var csEncrypt = new CryptoStream(msEncrypt, encryptor, CryptoStreamMode.Write);
                using (var swEncrypt = new StreamWriter(csEncrypt))
                {
                    swEncrypt.Write(plainText);
                }

                var encrypted = msEncrypt.ToArray();
                var result = new byte[aes.IV.Length + encrypted.Length];
                Buffer.BlockCopy(aes.IV, 0, result, 0, aes.IV.Length);
                Buffer.BlockCopy(encrypted, 0, result, aes.IV.Length, encrypted.Length);

                return Convert.ToBase64String(result);
            }
            catch (Exception ex)
            {
                _loggingService.LogError(ex, "Failed to encrypt data");
                throw;
            }
        }

        public string Decrypt(string encryptedText)
        {
            try
            {
                var fullCipher = Convert.FromBase64String(encryptedText);

                using var aes = Aes.Create();
                aes.Key = _encryptionKey;

                var iv = new byte[aes.BlockSize / 8];
                var cipher = new byte[fullCipher.Length - iv.Length];

                Buffer.BlockCopy(fullCipher, 0, iv, 0, iv.Length);
                Buffer.BlockCopy(fullCipher, iv.Length, cipher, 0, cipher.Length);

                using var decryptor = aes.CreateDecryptor(aes.Key, iv);
                using var msDecrypt = new MemoryStream(cipher);
                using var csDecrypt = new CryptoStream(msDecrypt, decryptor, CryptoStreamMode.Read);
                using var srDecrypt = new StreamReader(csDecrypt);
                return srDecrypt.ReadToEnd();
            }
            catch (Exception ex)
            {
                _loggingService.LogError(ex, "Failed to decrypt data");
                throw;
            }
        }

        public string HashPassword(string password, string? salt = null)
        {
            salt ??= Convert.ToBase64String(RandomNumberGenerator.GetBytes(SALT_SIZE));

            using var pbkdf2 = new Rfc2898DeriveBytes(password, Encoding.UTF8.GetBytes(salt), ITERATIONS, HashAlgorithmName.SHA256);
            var hash = pbkdf2.GetBytes(32);

            return $"{Convert.ToBase64String(hash)}:{salt}";
        }

        public bool VerifyPassword(string password, string hashedPassword)
        {
            try
            {
                var parts = hashedPassword.Split(':');
                if (parts.Length != 2)
                    return false;

                var hash = parts[0];
                var salt = parts[1];

                var newHash = HashPassword(password, salt);
                return newHash == hashedPassword;
            }
            catch
            {
                return false;
            }
        }

        public string GenerateJwtToken(string deviceId, TimeSpan expiry)
        {
            try
            {
                var key = new SymmetricSecurityKey(_encryptionKey);
                var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

                var claims = new[]
                {
                    new Claim("deviceId", deviceId),
                    new Claim("iat", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
                    new Claim("exp", DateTimeOffset.UtcNow.Add(expiry).ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64)
                };

                var token = new JwtSecurityToken(
                    issuer: "MISA.AI",
                    audience: "MISA.Clients",
                    claims: claims,
                    expires: DateTime.UtcNow.Add(expiry),
                    signingCredentials: credentials
                );

                return _tokenHandler.WriteToken(token);
            }
            catch (Exception ex)
            {
                _loggingService.LogError(ex, "Failed to generate JWT token");
                throw;
            }
        }

        public bool ValidateJwtToken(string token)
        {
            try
            {
                var key = new SymmetricSecurityKey(_encryptionKey);
                var validationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = "MISA.AI",
                    ValidAudience = "MISA.Clients",
                    IssuerSigningKey = key
                };

                _tokenHandler.ValidateToken(token, validationParameters, out SecurityToken validatedToken);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public string GenerateSecureRandomString(int length)
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
            var result = new char[length];
            var randomBytes = new byte[length];

            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(randomBytes);

            for (int i = 0; i < length; i++)
            {
                result[i] = chars[randomBytes[i] % chars.Length];
            }

            return new string(result);
        }

        public async Task LogSecurityEventAsync(string eventType, string? details = null, string? userId = null, string? deviceId = null)
        {
            try
            {
                Directory.CreateDirectory("logs");

                var logEntry = new
                {
                    Timestamp = DateTime.UtcNow,
                    EventType = eventType,
                    Details = details,
                    UserId = userId,
                    DeviceId = deviceId,
                    Source = "MISA.Core"
                };

                var logFile = $"logs/security_{DateTime.UtcNow:yyyy-MM-dd}.json";
                var json = JsonConvert.SerializeObject(logEntry, Formatting.Indented);
                await File.AppendAllTextAsync(logFile, json + Environment.NewLine);

                _loggingService.LogSecurityEvent(eventType, details, userId, deviceId);
            }
            catch (Exception ex)
            {
                _loggingService.LogError(ex, "Failed to log security event");
            }
        }

        public void RemoveDevice(string deviceId)
        {
            if (_deviceKeys.ContainsKey(deviceId))
            {
                _deviceKeys.Remove(deviceId);
                _ = Task.Run(() => SaveDeviceKeysAsync()); // Fire and forget
                _loggingService.LogInformation($"Device {deviceId} removed from trusted devices");
            }
        }

        public IEnumerable<string> GetTrustedDevices()
        {
            return _deviceKeys.Keys.ToList();
        }
    }
}