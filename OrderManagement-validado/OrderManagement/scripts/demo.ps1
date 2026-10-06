param([string]$BaseUrl = 'http://localhost:8080', [string]$AdminEmail = 'admin@example.com')
$ErrorActionPreference = 'Stop'
$adminPassword = Read-Host 'Senha do administrador' -MaskInput
function Send-Json($Method, $Path, $Body, $Token) {
    $headers = @{}
    if ($Token) { $headers.Authorization = "Bearer $Token" }
    $parameters = @{ Method = $Method; Uri = "$BaseUrl$Path"; Headers = $headers }
    if ($null -ne $Body) { $parameters.Body = ($Body | ConvertTo-Json -Depth 10); $parameters.ContentType = 'application/json' }
    Invoke-RestMethod @parameters
}
$admin = Send-Json POST '/api/auth/login' @{ email = $AdminEmail; password = $adminPassword } $null
$email = "demo-$([Guid]::NewGuid().ToString('N'))@example.com"
$password = "Demo-$([Guid]::NewGuid().ToString('N'))!"
$null = Send-Json POST '/api/auth/register' @{ name = 'Cliente demonstração'; email = $email; password = $password } $null
$customer = Send-Json POST '/api/auth/login' @{ email = $email; password = $password } $null
$product = Send-Json POST '/api/products' @{ name = 'Teclado'; price = 199.90 } $admin.accessToken
$order = Send-Json POST '/api/orders' @{ items = @(@{ productId = $product.id; quantity = 2 }) } $customer.accessToken
Write-Host "Pedido $($order.id): $($order.status), total $($order.total)"
foreach ($status in @('Pago', 'EmPreparo', 'Enviado', 'Entregue')) {
    $order = Send-Json PATCH "/api/orders/$($order.id)/status" @{ status = $status } $admin.accessToken
    Write-Host "Status: $($order.status)"
}
$request = Send-Json POST '/api/reports/sales' $null $admin.accessToken
$report = $null
for ($attempt = 0; $attempt -lt 30; $attempt++) {
    $report = Send-Json GET "/api/reports/sales/$($request.id)" $null $admin.accessToken
    if ($report.completedAt) { break }
    Start-Sleep -Seconds 2
}
if (!$report.completedAt) { throw 'Relatório ainda pendente. Consulte os logs da API/Hangfire.' }
$report | Format-List
