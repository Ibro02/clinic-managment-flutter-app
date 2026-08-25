import '../core/auth_session.dart';
import '../core/base_provider.dart';
import '../models/location.dart';

class LocationProvider extends BaseProvider<Location> {
  LocationProvider(AuthSession authSession) : super('Location', authSession);

  @override
  Location fromJson(Map<String, dynamic> json) => Location.fromJson(json);
}
