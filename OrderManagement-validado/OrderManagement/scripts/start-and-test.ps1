param()
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$docker = 'C:\Program Files\Docker\Docker\resources\bin\docker.exe'
if (!(Test-Path $docker)) { $docker = 'docker' }
& $docker info --format '{{.ServerVersion}}'
if ($LASTEXITCODE -ne 0) { throw 'Abra o Docker Desktop, aguarde iniciar e execute este script novamente.' }
function New-Secret {
    $bytes = New-Object byte[] 32
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try { $rng.GetBytes($bytes) } finally { $rng.Dispose() }
    return ([Convert]::ToBase64String($bytes) + 'aA1!')
}
if (!(Test-Path '.env')) {
    @("DB_PASSWORD=$(New-Secret)", "JWT_KEY=$(New-Secret)", 'ADMIN_EMAIL=admin@example.com', "ADMIN_PASSWORD=$(New-Secret)") | Set-Content -Encoding ASCII '.env'
}
$settings = @{}
Get-Content '.env' | ForEach-Object {
    if ($_ -match '^([A-Z_]+)=(.*)$') { $settings[$matches[1]] = $matches[2] }
}
& $docker compose up --build -d
if ($LASTEXITCODE -ne 0) { throw 'Falha ao iniciar containers. Confira a mensagem do Docker acima.' }
$base = 'http://localhost:8080'
$ready = $false
for ($i = 0; $i -lt 60; $i++) {
    try { $null = Invoke-RestMethod "$base/health" -TimeoutSec 3; $ready = $true; break } catch { Start-Sleep -Seconds 3 }
}
if (!$ready) { & $docker compose logs --tail 80 api; throw 'API ainda indisponível.' }
function Request($method, $path, $body, $token) {
    $args = @{ Method = $method; Uri = "$base$path" }
    if ($token) { $args.Headers = @{ Authorization = "Bearer $token" } }
    if ($null -ne $body) { $args.ContentType = 'application/json'; $args.Body = ($body | ConvertTo-Json -Depth 8) }
    Invoke-RestMethod @args
}
$admin = Request POST '/api/auth/login' @{ email = $settings.ADMIN_EMAIL; password = $settings.ADMIN_PASSWORD } $null
$email = "test-$([Guid]::NewGuid().ToString('N'))@example.com"
$password = New-Secret
$null = Request POST '/api/auth/register' @{ name = 'Cliente teste Docker'; email = $email; password = $password } $null
$client = Request POST '/api/auth/login' @{ email = $email; password = $password } $null
$product = Request POST '/api/products' @{ name = 'Produto teste Docker'; price = 199.90 } $admin.accessToken
$order = Request POST '/api/orders' @{ items = @(@{ productId = $product.id; quantity = 2 }) } $client.accessToken
$lines = @("Validacao Docker: $(Get-Date -Format o)", "Pedido: $($order.id)", "Total: $($order.total)", "Status: $($order.status)")
foreach ($status in @('Pago', 'EmPreparo', 'Enviado', 'Entregue')) {
    $order = Request PATCH "/api/orders/$($order.id)/status" @{ status = $status } $admin.accessToken
    if ($order.status -ne $status) { throw 'Status inesperado.' }
    $lines += "Status: $($order.status)"
}
$requested = Request POST '/api/reports/sales' $null $admin.accessToken
$report = $null
for ($i = 0; $i -lt 60; $i++) {
    $report = Request GET "/api/reports/sales/$($requested.id)" $null $admin.accessToken
    if ($report.completedAt) { break }
    Start-Sleep -Seconds 2
}
if (!$report.completedAt) { throw 'Relatorio nao foi processado pelo Hangfire dentro do prazo.' }
$lines += "Relatorio concluido pelo Hangfire: $($report.id)"
$lines += "Receita acumulada: $($report.revenue)"
$lines += 'Fluxo HTTP com SQL Server e relatorio Hangfire: APROVADO'
$lines | Set-Content -Encoding UTF8 'docker-validation.txt'
& $docker compose ps
$lines | Write-Output
Write-Host 'API pronta em http://localhost:8080. Credenciais locais no arquivo .env.'
