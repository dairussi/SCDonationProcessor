FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["SCDonationProcessor.EventProcessor/SCDonationProcessor.EventProcessor.csproj", "SCDonationProcessor.EventProcessor/"]
COPY ["SCDonationProcessor.Application/SCDonationProcessor.Application.csproj", "SCDonationProcessor.Application/"]
COPY ["SCDonationProcessor.Infrastructure/SCDonationProcessor.Infrastructure.csproj", "SCDonationProcessor.Infrastructure/"]
COPY ["SCDonationProcessor.Domain/SCDonationProcessor.Domain.csproj", "SCDonationProcessor.Domain/"]

RUN dotnet restore "SCDonationProcessor.EventProcessor/SCDonationProcessor.EventProcessor.csproj"

COPY . .

RUN dotnet publish "SCDonationProcessor.EventProcessor/SCDonationProcessor.EventProcessor.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

COPY --from=build /app/publish .

EXPOSE 9091

ENTRYPOINT ["dotnet", "SCDonationProcessor.EventProcessor.dll"]
