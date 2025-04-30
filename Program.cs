var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Register custom services
builder.Services.AddScoped<gentest.Services.ISwaggerFileService, gentest.Services.SwaggerFileService>();
builder.Services.AddScoped<gentest.Services.ITestGenerationService, gentest.Services.TestGenerationService>();
builder.Services.AddScoped<gentest.Services.ITestExecutionService, gentest.Services.TestExecutionService>();

var app = builder.Build();
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
app.UseHttpsRedirection();
app.Run();

