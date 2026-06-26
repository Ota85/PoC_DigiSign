var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();

// Register a named HttpClient for the DigiSign Identify API.
builder.Services.AddHttpClient("DigiSign", client =>
{
    var cfg = builder.Configuration.GetSection("DigiSign");
    client.BaseAddress = new Uri(cfg["BaseUrl"]!);
    client.DefaultRequestHeaders.Authorization =
        new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", cfg["BearerToken"]);
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();
app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();

app.Run();
