FROM mcr.microsoft.com/dotnet/sdk:8.0-alpine AS builder

WORKDIR /app

# Restore first for better layer caching (layered Clean Architecture paths)
COPY src/Core/Stock.Domain/Stock.Domain.csproj src/Core/Stock.Domain/
COPY src/Core/Stock.Application/Stock.Application.csproj src/Core/Stock.Application/
COPY src/Infrastructure/Stock.Infrastructure/Stock.Infrastructure.csproj src/Infrastructure/Stock.Infrastructure/
COPY src/Presentation/Stock.Api/Stock.Api.csproj src/Presentation/Stock.Api/
RUN dotnet restore src/Presentation/Stock.Api/Stock.Api.csproj

COPY src/ src/
RUN dotnet publish src/Presentation/Stock.Api/Stock.Api.csproj -c Release -o /out --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine

WORKDIR /app
COPY --from=builder /out .

ENV ASPNETCORE_URLS=http://+:8083
EXPOSE 8083

# .NET 8 images ship with a built-in non-root "app" user
USER app

ENTRYPOINT ["dotnet", "Stock.Api.dll"]
