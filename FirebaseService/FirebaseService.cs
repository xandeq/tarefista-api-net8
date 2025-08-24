using Amazon;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using FirebaseAdmin;
using Google.Apis.Auth.OAuth2;
using Google.Cloud.Firestore;
using Google.Cloud.Firestore.V1;
using System.Text.Json;

public class FirebaseService
{
    private readonly IConfiguration _config;
    private readonly IAmazonSecretsManager _secretsManager;
    private FirestoreDb _firestoreDb;

    public FirebaseService(IConfiguration config)
    {
        _config = config;

        var accessKey = _config["AWS:AccessKeyId"];
        var secretKey = _config["AWS:SecretAccessKey"];

        if (string.IsNullOrEmpty(accessKey) || string.IsNullOrEmpty(secretKey))
            throw new Exception("AWS credentials not found in configuration.");

        _secretsManager = new AmazonSecretsManagerClient(accessKey, secretKey, RegionEndpoint.USEast1);
    }

    public async Task InitializeAsync()
    {
        var serviceAccount = await GetFirebaseCredentialsAsync();

        if (FirebaseApp.DefaultInstance == null)
        {
            FirebaseApp.Create(new AppOptions
            {
                Credential = GoogleCredential.FromJson(serviceAccount)
            });
        }

        // 🔹 Usa FirestoreClientBuilder com credenciais
        var credential = GoogleCredential.FromJson(serviceAccount)
            .CreateScoped(FirestoreClient.DefaultScopes);

        var client = new FirestoreClientBuilder
        {
            Credential = credential
        }.Build();

        _firestoreDb = FirestoreDb.Create("tarefista", client);

        Console.WriteLine("Firebase inicializado com sucesso.");
    }

    private async Task<string> GetFirebaseCredentialsAsync()
    {
        try
        {
            var request = new GetSecretValueRequest
            {
                SecretId = "firebaseServiceAccountKey",
                VersionStage = "AWSCURRENT"
            };

            var response = await _secretsManager.GetSecretValueAsync(request);

            Console.WriteLine("Secret recuperado com sucesso.");
            var secretObject = JsonSerializer.Deserialize<Dictionary<string, string>>(response.SecretString);

            return secretObject["firebaseServiceAccountKey"];
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Erro ao buscar secret: {ex.Message}");
            throw;
        }
    }

    public FirestoreDb GetFirestoreDb()
    {
        if (_firestoreDb == null)
            throw new InvalidOperationException("Firebase não inicializado. Chame InitializeAsync() primeiro.");

        return _firestoreDb;
    }
}
