import '../core/auth_session.dart';
import '../core/base_provider.dart';
import '../models/city.dart';

class CityProvider extends BaseProvider<City> {
  CityProvider(AuthSession authSession) : super('City', authSession);

  @override
  City fromJson(Map<String, dynamic> json) => City.fromJson(json);
}
